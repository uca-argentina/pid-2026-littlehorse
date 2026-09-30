using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Orders;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// US-22: the customer's tracking screen hears its order move instead of
/// asking every few seconds. Held to the same rule as the tracking link: only
/// whoever holds an order's token hears about that order.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class TrackingHubTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task OrderChanged_WhenTheOrderItFollowsMoves_IsReceived()
    {
        TrackingToken token = TrackingToken.New();

        await using HubConnection connection = await Following(token.Value);

        TaskCompletionSource notified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(TrackingHub.OrderChanged, () => notified.TrySetResult());

        await Followers().NotifyOrderChangedAsync(token, CancellationToken.None);

        await notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // Somebody else's order moving is none of this phone's business — not
    // even the fact that it moved.
    [Fact]
    public async Task OrderChanged_WhenAnotherOrderMoves_IsNeverReceived()
    {
        TrackingToken mine = TrackingToken.New();
        TrackingToken theirs = TrackingToken.New();

        await using HubConnection mineConnection = await Following(mine.Value);
        await using HubConnection theirsConnection = await Following(theirs.Value);

        TaskCompletionSource mineNotified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource theirsNotified = new(TaskCreationOptions.RunContinuationsAsynchronously);
        mineConnection.On(TrackingHub.OrderChanged, () => mineNotified.TrySetResult());
        theirsConnection.On(TrackingHub.OrderChanged, () => theirsNotified.TrySetResult());

        await Followers().NotifyOrderChangedAsync(mine, CancellationToken.None);
        await mineNotified.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A generous window for the wrong message to arrive before declaring
        // the silence meaningful — the failure mode of a race is a flaky pass,
        // not a flaky fail.
        await Task.WhenAny(theirsNotified.Task, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.False(theirsNotified.Task.IsCompleted);
    }

    // One connection, one order, like the screen it serves. Otherwise a single
    // connection could join groups without end and keep them all in memory.
    [Fact]
    public async Task Follow_WhenAnotherOrderIsFollowedLater_StopsHearingTheFirst()
    {
        TrackingToken first = TrackingToken.New();
        TrackingToken second = TrackingToken.New();

        await using HubConnection connection = await Following(first.Value);
        await connection.InvokeAsync(nameof(TrackingHub.Follow), second.Value);

        TaskCompletionSource heard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(TrackingHub.OrderChanged, () => heard.TrySetResult());

        await Followers().NotifyOrderChangedAsync(first, CancellationToken.None);
        await Task.WhenAny(heard.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.False(heard.Task.IsCompleted);

        // Still following the second: letting go of the first is not letting go of everything.
        await Followers().NotifyOrderChangedAsync(second, CancellationToken.None);
        await heard.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // Whatever a stranger sends is an answer, not a crash: a link somebody
    // mangled while copying it must not take the connection down.
    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task Follow_WhenTheTokenIsMalformed_KeepsTheConnectionOpen(string token)
    {
        await using HubConnection connection = await Following(token);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

    private IOrderFollowers Followers() => _factory.Services.GetRequiredService<IOrderFollowers>();

    /// <summary>
    /// Connected with no token at all — the customer has no account — and
    /// following once the invocation returns, which is after the hub put it
    /// in its group: nothing sent from here on can be missed.
    /// </summary>
    private async Task<HubConnection> Following(string token)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, TrackingHubRoute.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // The in-memory TestServer has no real socket to upgrade, so the
                // transport SignalR would normally pick first does not apply here.
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        await connection.InvokeAsync(nameof(TrackingHub.Follow), token);

        return connection;
    }
}
