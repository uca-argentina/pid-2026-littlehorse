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

    // US-34: the menu follows every order of the night on one connection.
    [Fact]
    public async Task Follow_WhenSeveralOrdersAreFollowed_HearsEachOfThem()
    {
        TrackingToken first = TrackingToken.New();
        TrackingToken second = TrackingToken.New();

        await using HubConnection connection = await Following(first.Value);
        await connection.InvokeAsync(nameof(TrackingHub.Follow), second.Value);

        int heard = 0;
        TaskCompletionSource heardBoth = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(TrackingHub.OrderChanged, () =>
        {
            if (Interlocked.Increment(ref heard) == 2) heardBoth.TrySetResult();
        });

        await Followers().NotifyOrderChangedAsync(first, CancellationToken.None);
        await Followers().NotifyOrderChangedAsync(second, CancellationToken.None);

        await heardBoth.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // A few orders, not without end: otherwise a single connection could join
    // groups for invented tokens and keep them all in the server's memory.
    [Fact]
    public async Task Follow_WhenMoreOrdersThanTheCapAreFollowed_LetsGoOfTheOldest()
    {
        TrackingToken[] tokens = [.. Enumerable.Range(0, TrackingHub.MostFollowedPerConnection + 1).Select(_ => TrackingToken.New())];

        await using HubConnection connection = await Following(tokens[0].Value);
        foreach (TrackingToken token in tokens.Skip(1)) await connection.InvokeAsync(nameof(TrackingHub.Follow), token.Value);

        TaskCompletionSource heard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(TrackingHub.OrderChanged, () => heard.TrySetResult());

        await Followers().NotifyOrderChangedAsync(tokens[0], CancellationToken.None);
        await Task.WhenAny(heard.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.False(heard.Task.IsCompleted);

        // The newest is still followed: making room is not letting go of everything.
        await Followers().NotifyOrderChangedAsync(tokens[^1], CancellationToken.None);
        await heard.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // The tracking screen follows the same order again on every reconnect and
    // every answer: that is one order, not one more each time.
    [Fact]
    public async Task Follow_WhenTheSameOrderIsFollowedAgain_TakesNoMoreRoom()
    {
        TrackingToken first = TrackingToken.New();
        TrackingToken again = TrackingToken.New();

        await using HubConnection connection = await Following(first.Value);
        for (int i = 0; i < TrackingHub.MostFollowedPerConnection; i++) await connection.InvokeAsync(nameof(TrackingHub.Follow), again.Value);

        TaskCompletionSource heard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(TrackingHub.OrderChanged, () => heard.TrySetResult());

        await Followers().NotifyOrderChangedAsync(first, CancellationToken.None);
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
