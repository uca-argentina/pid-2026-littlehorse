using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Kds;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DrinkIt.Api.IntegrationTests.Kds;

/// <summary>
/// US-15's own invariant, and CLAUDE.md's for every table with a VenueId: a
/// boliche's tablet must never learn about another boliche's queue. Everything
/// else about the hub is plumbing; this is the one thing worth a real client.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class KdsHubIsolationTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly KdsHubTestFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task BoardChanged_WhenAnotherVenuesQueueChanges_IsNeverReceived()
    {
        Guid mine = Guid.CreateVersion7();
        Guid theirs = Guid.CreateVersion7();

        await using HubConnection mineConnection = await ConnectedAsKds(mine);
        await using HubConnection theirsConnection = await ConnectedAsKds(theirs);

        TaskCompletionSource mineNotified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource theirsNotified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        mineConnection.On(KdsHub.BoardChanged, () => mineNotified.TrySetResult());
        theirsConnection.On(KdsHub.BoardChanged, () => theirsNotified.TrySetResult());

        IKdsBoardNotifier notifier = _factory.Services.GetRequiredService<IKdsBoardNotifier>();
        await notifier.NotifyBoardChangedAsync(mine, CancellationToken.None);

        await mineNotified.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A generous window for the wrong message to arrive before declaring
        // the silence meaningful — the failure mode of a race is a flaky pass,
        // not a flaky fail.
        await Task.WhenAny(theirsNotified.Task, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.False(theirsNotified.Task.IsCompleted);
    }

    [Fact]
    public async Task BoardChanged_WhenItsOwnVenuesQueueChanges_IsReceived()
    {
        Guid venueId = Guid.CreateVersion7();

        await using HubConnection connection = await ConnectedAsKds(venueId);

        TaskCompletionSource notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(KdsHub.BoardChanged, () => notified.TrySetResult());

        IKdsBoardNotifier notifier = _factory.Services.GetRequiredService<IKdsBoardNotifier>();
        await notifier.NotifyBoardChangedAsync(venueId, CancellationToken.None);

        await notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task<HubConnection> ConnectedAsKds(Guid venueId)
    {
        ITokenIssuer tokenIssuer = _factory.Services.GetRequiredService<ITokenIssuer>();
        AccessToken token = tokenIssuer.Issue(Guid.CreateVersion7(), venueId, "kds", StaffRole.Kds);

        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, KdsHubRoute.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // The in-memory TestServer has no real socket to upgrade, so the
                // transport SignalR would normally pick first does not apply here.
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token.Value);
            })
            .Build();

        await connection.StartAsync();

        return connection;
    }

    /// <summary>
    /// Points the whole app — migrations, seeding, everything Program.cs does
    /// before it starts listening — at the same Testcontainers instance the
    /// query tests already run against, instead of the connection string in
    /// appsettings.json.
    /// </summary>
    private sealed class KdsHubTestFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:DrinkIt", connectionString),
            ]));
        }
    }
}
