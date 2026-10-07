using System.Security.Claims;
using System.Text;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using DrinkIt.Infrastructure.Kds;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DrinkIt.Api.IntegrationTests.Kds;

/// <summary>
/// US-15's own invariant, and CLAUDE.md's for every table with a VenueId: a
/// boliche's tablet must never learn about another boliche's queue. Everything
/// else about the hub is plumbing; this is the one thing worth a real client.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class KdsHubIsolationTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task BoardChanged_WhenAnotherVenuesQueueChanges_IsNeverReceived()
    {
        (Guid mine, Guid mineAccount) = await SeedTonight.AnAccountWorkingTonight(sql, StaffRole.Kds);
        (Guid theirs, Guid theirsAccount) = await SeedTonight.AnAccountWorkingTonight(sql, StaffRole.Kds);

        await using HubConnection mineConnection = await ConnectedAsKds(mine, mineAccount);
        await using HubConnection theirsConnection = await ConnectedAsKds(theirs, theirsAccount);

        TaskCompletionSource mineNotified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource theirsNotified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        mineConnection.On(KdsHub.BoardChanged, () => mineNotified.TrySetResult());
        theirsConnection.On(KdsHub.BoardChanged, () => theirsNotified.TrySetResult());

        IKdsBoardNotifier notifier = _factory.Services.GetRequiredService<IKdsBoardNotifier>();
        await UntilHeard(() => notifier.NotifyBoardChangedAsync(mine, CancellationToken.None), mineNotified.Task);

        // A generous window for the wrong message to arrive before declaring
        // the silence meaningful — the failure mode of a race is a flaky pass,
        // not a flaky fail.
        await Task.WhenAny(theirsNotified.Task, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.False(theirsNotified.Task.IsCompleted);
    }

    [Fact]
    public async Task BoardChanged_WhenItsOwnVenuesQueueChanges_IsReceived()
    {
        (Guid venueId, Guid account) = await SeedTonight.AnAccountWorkingTonight(sql, StaffRole.Kds);

        await using HubConnection connection = await ConnectedAsKds(venueId, account);

        TaskCompletionSource notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(KdsHub.BoardChanged, () => notified.TrySetResult());

        IKdsBoardNotifier notifier = _factory.Services.GetRequiredService<IKdsBoardNotifier>();
        await UntilHeard(() => notifier.NotifyBoardChangedAsync(venueId, CancellationToken.None), notified.Task);
    }

    // Refused at the door since US-35: no venue means no night, and so no crew
    // to be in. The hub's own check in OnConnectedAsync stays behind it.
    [Fact]
    public async Task StartAsync_WhenTheTokenCarriesNoVenue_IsRefused()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectedWith(SignedKdsTokenWithoutVenue()));
    }

    // US-35, criterion 4: right role, right venue, not in tonight's crew.
    [Fact]
    public async Task StartAsync_WhenTheAccountIsNotInTonightsCrew_IsRefused()
    {
        (Guid venueId, _) = await SeedTonight.AnAccountWorkingTonight(sql, StaffRole.Kds);
        Guid outsider = await SeedTonight.AnAccountOffTonight(sql, venueId, StaffRole.Kds);

        await Assert.ThrowsAnyAsync<Exception>(() => ConnectedAsKds(venueId, outsider));
    }

    private async Task<HubConnection> ConnectedAsKds(Guid venueId, Guid account)
    {
        ITokenIssuer tokenIssuer = _factory.Services.GetRequiredService<ITokenIssuer>();
        AccessToken token = tokenIssuer.Issue(account, venueId, "kds", StaffRole.Kds);

        return await ConnectedWith(token.Value);
    }

    /// <summary>
    /// Signed with the real key, so it gets past authentication and the role
    /// check — only the venue claim is missing. The issuer never writes one
    /// like this; a hub has to hold its ground anyway.
    /// </summary>
    private string SignedKdsTokenWithoutVenue()
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
                new Claim(JwtClaims.Name, "kds"),
                new Claim(JwtClaims.Role, nameof(StaffRole.Kds)),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    private async Task<HubConnection> ConnectedWith(string token)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, KdsHubRoute.Path), options =>
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
