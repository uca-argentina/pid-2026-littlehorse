using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Kds;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Kds;

/// <summary>
/// US-38, end to end over the hub: the tablet's beat reaches the database with
/// its own venue, which the hub can only take from the token — a hub method
/// runs in a scope of its own, where no middleware resolved anything.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class KdsHubBeatTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    // Opening the board already says the tablet is there: a bar does not
    // wait twenty seconds to start getting orders.
    [Fact]
    public async Task StartAsync_WhenTheTabletConnects_RecordsABeat()
    {
        (Guid venueId, Guid bar) = await SeedTonight.AnAccountWorkingTonight(sql, StaffRole.Kds);

        await using HubConnection connection = await ConnectedAsKds(venueId, bar);

        Assert.NotNull(await UntilBeaten(venueId, bar, after: null));
    }

    [Fact]
    public async Task Beat_WhenTheTabletBeats_MovesItsBeatForward()
    {
        (Guid venueId, Guid bar) = await SeedTonight.AnAccountWorkingTonight(sql, StaffRole.Kds);
        await using HubConnection connection = await ConnectedAsKds(venueId, bar);
        DateTimeOffset? first = await UntilBeaten(venueId, bar, after: null);

        await connection.InvokeAsync(KdsHub.Beat);

        Assert.NotNull(await UntilBeaten(venueId, bar, after: first));
    }

    /// <summary>
    /// The bar's last beat once it is later than <paramref name="after"/>, or
    /// null if that does not happen within five seconds. Connecting records it
    /// a moment after StartAsync returns, so it is waited for, not read once.
    /// </summary>
    private async Task<DateTimeOffset?> UntilBeaten(Guid venueId, Guid bar, DateTimeOffset? after)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            await using DrinkItDbContext read = sql.CreateContext(venueId);
            DateTimeOffset? beat = await read.KdsBeats
                .Where(row => row.KdsId == bar)
                .Select(row => (DateTimeOffset?)row.LastBeatAt)
                .SingleOrDefaultAsync();

            if (beat is not null && (after is null || beat > after)) return beat;

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        return null;
    }

    private async Task<HubConnection> ConnectedAsKds(Guid venueId, Guid account)
    {
        ITokenIssuer tokenIssuer = _factory.Services.GetRequiredService<ITokenIssuer>();
        AccessToken token = tokenIssuer.Issue(account, venueId, "kds", StaffRole.Kds);

        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, KdsHubRoute.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // The in-memory TestServer has no real socket to upgrade.
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token.Value);
            })
            .Build();

        await connection.StartAsync();

        return connection;
    }
}
