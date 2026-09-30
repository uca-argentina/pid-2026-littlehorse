using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Common;

/// <summary>
/// US-15: where "the handlers react" (CLAUDE.md's Domain Events + Observer)
/// actually lives — the one place that knows which event wakes which port.
/// </summary>
public class DomainEventDispatcherTests
{
    private static readonly TrackingToken AToken = TrackingToken.New();

    [Fact]
    public async Task DispatchAsync_WhenAnOrderIsQueued_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill(), new SpyFollowers());

        await dispatcher.DispatchAsync([new OrderQueued(venueId, AToken)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    // US-16: taking an order and handing it back both change which column it
    // is in, and every tablet of the venue has to redraw.
    [Fact]
    public async Task DispatchAsync_WhenTheBarTakesAnOrder_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill(), new SpyFollowers());

        await dispatcher.DispatchAsync([new OrderPreparationStarted(venueId, AToken)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    [Fact]
    public async Task DispatchAsync_WhenAnOrderGoesBackToTheQueue_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill(), new SpyFollowers());

        await dispatcher.DispatchAsync([new OrderRequeued(venueId, AToken)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    // US-18: every move out of preparation changes a column too, on every
    // tablet of the venue.
    public static TheoryData<IDomainEvent> MovesOnTheBoard(Guid venueId) =>
    [
        new OrderReady(venueId, Guid.CreateVersion7(), AToken),
        new OrderReturnedToPreparation(venueId, AToken),
        new OrderDelivered(venueId, AToken),
        new OrderDeliveryUndone(venueId, AToken),
    ];

    [Fact]
    public async Task DispatchAsync_WhenAnOrderMovesOutOfPreparation_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();

        foreach (IDomainEvent moved in MovesOnTheBoard(venueId))
        {
            SpyNotifier notifier = new();

            await new DomainEventDispatcher(notifier, new SpyTill(), new SpyFollowers()).DispatchAsync([moved], CancellationToken.None);

            Assert.True(notifier.NotifiedVenues.SequenceEqual([venueId]), $"{moved.GetType().Name} did not reach the board.");
        }
    }

    // US-26: the till's "Por cobrar" updates on its own — a new order to
    // collect, or one another till just collected.
    [Theory]
    [InlineData(nameof(OrderAwaitingPayment))]
    [InlineData(nameof(OrderCollected))]
    public async Task DispatchAsync_WhenTheCashWaitingChanges_NotifiesTheTillAndNotTheBoard(string what)
    {
        Guid venueId = Guid.CreateVersion7();
        IDomainEvent changed = what == nameof(OrderCollected) ? new OrderCollected(venueId, AToken) : new OrderAwaitingPayment(venueId, AToken);
        SpyNotifier board = new();
        SpyTill till = new();

        await new DomainEventDispatcher(board, till, new SpyFollowers()).DispatchAsync([changed], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(till.NotifiedVenues));
        Assert.Empty(board.NotifiedVenues);
    }

    [Fact]
    public async Task DispatchAsync_WhenTheBarsBoardChanges_LeavesTheTillAlone()
    {
        SpyTill till = new();

        await new DomainEventDispatcher(new SpyNotifier(), till, new SpyFollowers())
            .DispatchAsync([new OrderQueued(Guid.CreateVersion7(), AToken)], CancellationToken.None);

        Assert.Empty(till.NotifiedVenues);
    }

    // US-22: whoever holds the order's link hears of every move it makes, the
    // till's and the bar's alike, and hears it by that link's token.
    [Fact]
    public async Task DispatchAsync_WhenAnOrderChanges_NotifiesWhoeverFollowsIt()
    {
        TrackingToken token = TrackingToken.New();
        SpyFollowers followers = new();

        await new DomainEventDispatcher(new SpyNotifier(), new SpyTill(), followers)
            .DispatchAsync([new OrderPreparationStarted(Guid.CreateVersion7(), token)], CancellationToken.None);

        Assert.Equal(token, Assert.Single(followers.Notified));
    }

    // Collecting cash raises two events for one move — queued and collected.
    // The phone would ask twice for the same answer.
    [Fact]
    public async Task DispatchAsync_WhenOneOrderRaisesSeveralEvents_NotifiesItsFollowersOnce()
    {
        Guid venueId = Guid.CreateVersion7();
        TrackingToken token = TrackingToken.New();
        SpyFollowers followers = new();

        await new DomainEventDispatcher(new SpyNotifier(), new SpyTill(), followers)
            .DispatchAsync([new OrderQueued(venueId, token), new OrderCollected(venueId, token)], CancellationToken.None);

        Assert.Equal(token, Assert.Single(followers.Notified));
    }

    // A future event this dispatcher does not yet know how to react to must
    // not throw: it is simply not its reaction to make, same as an unhandled
    // status in a switch that only some callers care about.
    [Fact]
    public async Task DispatchAsync_WhenTheEventIsNotOneItReactsTo_DoesNothing()
    {
        SpyNotifier notifier = new();
        SpyFollowers followers = new();

        await new DomainEventDispatcher(notifier, new SpyTill(), followers).DispatchAsync([new SomeOtherEvent()], CancellationToken.None);

        Assert.Empty(notifier.NotifiedVenues);
        Assert.Empty(followers.Notified);
    }

    private sealed record SomeOtherEvent : IDomainEvent;

    private sealed class SpyNotifier : IKdsBoardNotifier
    {
        public List<Guid> NotifiedVenues { get; } = [];

        public Task NotifyBoardChangedAsync(Guid venueId, CancellationToken cancellationToken)
        {
            NotifiedVenues.Add(venueId);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyTill : ITillNotifier
    {
        public List<Guid> NotifiedVenues { get; } = [];

        public Task NotifyTillChangedAsync(Guid venueId, CancellationToken cancellationToken)
        {
            NotifiedVenues.Add(venueId);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyFollowers : IOrderFollowers
    {
        public List<TrackingToken> Notified { get; } = [];

        public Task NotifyOrderChangedAsync(TrackingToken order, CancellationToken cancellationToken)
        {
            Notified.Add(order);
            return Task.CompletedTask;
        }
    }
}
