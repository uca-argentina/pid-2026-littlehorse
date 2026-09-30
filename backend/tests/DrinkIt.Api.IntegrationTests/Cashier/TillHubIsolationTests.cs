using System.Security.Claims;
using System.Text;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Application.Cashier;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using DrinkIt.Infrastructure.Cashier;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DrinkIt.Api.IntegrationTests.Cashier;

/// <summary>
/// The till's live "Por cobrar" (US-26), held to CLAUDE.md's invariant: a
/// boliche's till must never learn about another boliche's orders, and only a
/// cashier gets in.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class TillHubIsolationTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task TillChanged_WhenAnotherVenuesCashChanges_IsNeverReceived()
    {
        Guid mine = Guid.CreateVersion7();
        Guid theirs = Guid.CreateVersion7();

        await using HubConnection mineConnection = await ConnectedAsCashier(mine);
        await using HubConnection theirsConnection = await ConnectedAsCashier(theirs);

        TaskCompletionSource mineNotified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource theirsNotified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        mineConnection.On(TillHub.TillChanged, () => mineNotified.TrySetResult());
        theirsConnection.On(TillHub.TillChanged, () => theirsNotified.TrySetResult());

        ITillNotifier notifier = _factory.Services.GetRequiredService<ITillNotifier>();
        await UntilHeard(() => notifier.NotifyTillChangedAsync(mine, CancellationToken.None), mineNotified.Task);

        // A generous window for the wrong message to arrive before declaring
        // the silence meaningful — the failure mode of a race is a flaky pass,
        // not a flaky fail.
        await Task.WhenAny(theirsNotified.Task, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.False(theirsNotified.Task.IsCompleted);
    }

    [Fact]
    public async Task TillChanged_WhenItsOwnVenuesCashChanges_IsReceived()
    {
        Guid venueId = Guid.CreateVersion7();

        await using HubConnection connection = await ConnectedAsCashier(venueId);

        TaskCompletionSource notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(TillHub.TillChanged, () => notified.TrySetResult());

        ITillNotifier notifier = _factory.Services.GetRequiredService<ITillNotifier>();
        await UntilHeard(() => notifier.NotifyTillChangedAsync(venueId, CancellationToken.None), notified.Task);
    }

    [Fact]
    public async Task OnConnectedAsync_WhenTheTokenCarriesNoVenue_ClosesTheConnection()
    {
        await using HubConnection connection = await ConnectedWith(SignedCashierTokenWithoutVenue());

        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        // Already closed by the time the handler was attached counts too.
        if (connection.State == HubConnectionState.Disconnected) closed.TrySetResult();

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // The bar's tablet has a token of this venue, and still no business here.
    [Fact]
    public async Task StartAsync_WhenTheTokenIsTheBars_IsRefused()
    {
        ITokenIssuer tokenIssuer = _factory.Services.GetRequiredService<ITokenIssuer>();
        AccessToken token = tokenIssuer.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "kds", StaffRole.Kds);

        await Assert.ThrowsAnyAsync<Exception>(() => ConnectedWith(token.Value));
    }

    private async Task<HubConnection> ConnectedAsCashier(Guid venueId)
    {
        ITokenIssuer tokenIssuer = _factory.Services.GetRequiredService<ITokenIssuer>();
        AccessToken token = tokenIssuer.Issue(Guid.CreateVersion7(), venueId, "laura.caja", StaffRole.Cashier);

        return await ConnectedWith(token.Value);
    }

    /// <summary>
    /// Signed with the real key, so it gets past authentication and the role
    /// check — only the venue claim is missing. The issuer never writes one
    /// like this; a hub has to hold its ground anyway.
    /// </summary>
    private string SignedCashierTokenWithoutVenue()
    {
        JwtOptions settings = _factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        DateTime now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
                new Claim(JwtClaims.Name, "laura.caja"),
                new Claim(JwtClaims.Role, nameof(StaffRole.Cashier)),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    private async Task<HubConnection> ConnectedWith(string token)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, TillHubRoute.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // The in-memory TestServer has no real socket to upgrade, so the
                // transport SignalR would normally pick first does not apply here.
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        await connection.StartAsync();

        return connection;
    }

    /// <summary>
    /// Sends until the message is heard, for at most ten seconds. StartAsync
    /// returns once the handshake is done, and the hub puts the connection in
    /// its group a moment later: a message sent in between is lost, and a test
    /// that sends it once fails whenever it wins that race.
    /// </summary>
    private static async Task UntilHeard(Func<Task> send, Task heard)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        while (!heard.IsCompleted)
        {
            await send();
            await Task.WhenAny(heard, Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token));
            timeout.Token.ThrowIfCancellationRequested();
        }
    }
}
