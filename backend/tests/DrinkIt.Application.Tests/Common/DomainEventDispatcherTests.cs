using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Waiter;
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

    private static readonly Guid ABar = Guid.CreateVersion7();

    [Fact]
    public async Task DispatchAsync_WhenAnOrderIsQueued_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill(), new SpyFollowers(), new SpyWaiters());

        await dispatcher.DispatchAsync([new OrderQueued(venueId, ABar, AToken)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    // US-16: taking an order and handing it back both change which column it
    // is in, and every tablet of the venue has to redraw.
    [Fact]
    public async Task DispatchAsync_WhenTheBarTakesAnOrder_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill(), new SpyFollowers(), new SpyWaiters());

        await dispatcher.DispatchAsync([new OrderPreparationStarted(venueId, ABar, AToken)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    [Fact]
    public async Task DispatchAsync_WhenAnOrderGoesBackToTheQueue_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill(), new SpyFollowers(), new SpyWaiters());

        await dispatcher.DispatchAsync([new OrderRequeued(venueId, ABar, AToken)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    // US-18: every move out of preparation changes a column too, on every
    // tablet of the venue.
    public static TheoryData<IDomainEvent> MovesOnTheBoard(Guid venueId) =>
    [
        new OrderReady(venueId, ABar, Guid.CreateVersion7(), AToken),
        new OrderReturnedToPreparation(venueId, ABar, AToken),
        new OrderDelivered(venueId, ABar, AToken),
        new OrderDeliveryUndone(venueId, ABar, AToken),
    ];

    [Fact]
    public async Task DispatchAsync_WhenAnOrderMovesOutOfPreparation_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();

        foreach (IDomainEvent moved in MovesOnTheBoard(venueId))
        {
            SpyNotifier notifier = new();

            await new DomainEventDispatcher(notifier, new SpyTill(), new SpyFollowers(), new SpyWaiters()).DispatchAsync([moved], CancellationToken.None);

            Assert.True(notifier.NotifiedVenues.SequenceEqual([venueId]), $"{moved.GetType().Name} did not reach the board.");
        }
    }

    // US-36: each bar's board shows only its own orders, so a move redraws the
    // board of the order's bar and no other.
    [Fact]
    public async Task DispatchAsync_WhenAnOrderMovesOnTheBoard_NotifiesTheBoardOfItsBarOnly()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();

        await new DomainEventDispatcher(notifier, new SpyTill(), new SpyFollowers(), new SpyWaiters())
            .DispatchAsync([new OrderPreparationStarted(venueId, ABar, AToken)], CancellationToken.None);

        Assert.Equal((venueId, ABar), Assert.Single(notifier.Notified));
    }

    // An order from before bars kept stock is on no board, so no board redraws.
    [Fact]
    public async Task DispatchAsync_WhenTheOrderHasNoBar_NotifiesNoBoard()
    {
        SpyNotifier notifier = new();

        await new DomainEventDispatcher(notifier, new SpyTill(), new SpyFollowers(), new SpyWaiters())
            .DispatchAsync([new OrderDelivered(Guid.CreateVersion7(), null, AToken)], CancellationToken.None);

        Assert.Empty(notifier.Notified);
    }

    // US-26: the till's "Por cobrar" updates on its own — a new order to
    // collect, or one another till just collected. US-23: or one canceled,
    // at another till or from the customer's phone.
    [Theory]
    [InlineData(nameof(OrderAwaitingPayment))]
    [InlineData(nameof(OrderCollected))]
    [InlineData(nameof(OrderCanceled))]
    public async Task DispatchAsync_WhenTheCashWaitingChanges_NotifiesTheTillAndNotTheBoard(string what)
    {
        Guid venueId = Guid.CreateVersion7();
        IDomainEvent changed = what switch
        {
            nameof(OrderCollected) => new OrderCollected(venueId, AToken),
            nameof(OrderCanceled) => new OrderCanceled(venueId, AToken),
            _ => new OrderAwaitingPayment(venueId, AToken),
        };
        SpyNotifier board = new();
        SpyTill till = new();

        await new DomainEventDispatcher(board, till, new SpyFollowers(), new SpyWaiters()).DispatchAsync([changed], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(till.NotifiedVenues));
        Assert.Empty(board.NotifiedVenues);
    }

    [Fact]
    public async Task DispatchAsync_WhenTheBarsBoardChanges_LeavesTheTillAlone()
    {
        SpyTill till = new();

        await new DomainEventDispatcher(new SpyNotifier(), till, new SpyFollowers(), new SpyWaiters())
            .DispatchAsync([new OrderQueued(Guid.CreateVersion7(), ABar, AToken)], CancellationToken.None);

        Assert.Empty(till.NotifiedVenues);
    }

    // US-22: whoever holds the order's link hears of every move it makes, the
    // till's and the bar's alike, and hears it by that link's token.
    [Fact]
    public async Task DispatchAsync_WhenAnOrderChanges_NotifiesWhoeverFollowsIt()
    {
        TrackingToken token = TrackingToken.New();
        SpyFollowers followers = new();

        await new DomainEventDispatcher(new SpyNotifier(), new SpyTill(), followers, new SpyWaiters())
            .DispatchAsync([new OrderPreparationStarted(Guid.CreateVersion7(), ABar, token)], CancellationToken.None);

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

        await new DomainEventDispatcher(new SpyNotifier(), new SpyTill(), followers, new SpyWaiters())
            .DispatchAsync([new OrderQueued(venueId, ABar, token), new OrderCollected(venueId, token)], CancellationToken.None);

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

        await new DomainEventDispatcher(notifier, new SpyTill(), followers, new SpyWaiters()).DispatchAsync([new SomeOtherEvent()], CancellationToken.None);

        Assert.Empty(notifier.NotifiedVenues);
        Assert.Empty(followers.Notified);
    }

    // US-43: what the waiters see changed, and every waiter of the venue redraws.
    [Fact]
    public async Task DispatchAsync_WhenATableOrderChanges_NotifiesTheWaitersOfTheVenue()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyWaiters waiters = new();
        DomainEventDispatcher dispatcher = new(new SpyNotifier(), new SpyTill(), new SpyFollowers(), waiters);

        await dispatcher.DispatchAsync([new TableOrderChanged(venueId, AToken)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(waiters.NotifiedVenues));
    }

    // A bar order changing column is the bar's business: no waiter has it on a list.
    [Fact]
    public async Task DispatchAsync_WhenTheBarMovesAnOrder_LeavesTheWaitersAlone()
    {
        SpyWaiters waiters = new();
        DomainEventDispatcher dispatcher = new(new SpyNotifier(), new SpyTill(), new SpyFollowers(), waiters);

        await dispatcher.DispatchAsync([new OrderReady(Guid.CreateVersion7(), ABar, Guid.CreateVersion7(), AToken)], CancellationToken.None);

        Assert.Empty(waiters.NotifiedVenues);
    }

    private sealed record SomeOtherEvent : IDomainEvent;

    private sealed class SpyWaiters : IWaiterBoardNotifier
    {
        public List<Guid> NotifiedVenues { get; } = [];

        public Task NotifyBoardChangedAsync(Guid venueId, CancellationToken cancellationToken)
        {
            NotifiedVenues.Add(venueId);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyNotifier : IKdsBoardNotifier
    {
        public List<(Guid VenueId, Guid KdsId)> Notified { get; } = [];

        public IEnumerable<Guid> NotifiedVenues => Notified.Select(board => board.VenueId);

        public Task NotifyBoardChangedAsync(Guid venueId, Guid kdsId, CancellationToken cancellationToken)
        {
            Notified.Add((venueId, kdsId));
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
