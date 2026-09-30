using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Common;

/// <summary>
/// US-15: where "the handlers react" (CLAUDE.md's Domain Events + Observer)
/// actually lives — the one place that knows which event wakes which port.
/// </summary>
public class DomainEventDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_WhenAnOrderIsQueued_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill());

        await dispatcher.DispatchAsync([new OrderQueued(venueId)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    // US-16: taking an order and handing it back both change which column it
    // is in, and every tablet of the venue has to redraw.
    [Fact]
    public async Task DispatchAsync_WhenTheBarTakesAnOrder_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill());

        await dispatcher.DispatchAsync([new OrderPreparationStarted(venueId)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    [Fact]
    public async Task DispatchAsync_WhenAnOrderGoesBackToTheQueue_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill());

        await dispatcher.DispatchAsync([new OrderRequeued(venueId)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    // US-18: every move out of preparation changes a column too, on every
    // tablet of the venue.
    public static TheoryData<IDomainEvent> MovesOnTheBoard(Guid venueId) =>
    [
        new OrderReady(venueId, Guid.CreateVersion7()),
        new OrderReturnedToPreparation(venueId),
        new OrderDelivered(venueId),
        new OrderDeliveryUndone(venueId),
    ];

    [Fact]
    public async Task DispatchAsync_WhenAnOrderMovesOutOfPreparation_NotifiesTheKdsBoard()
    {
        Guid venueId = Guid.CreateVersion7();

        foreach (IDomainEvent moved in MovesOnTheBoard(venueId))
        {
            SpyNotifier notifier = new();

            await new DomainEventDispatcher(notifier, new SpyTill()).DispatchAsync([moved], CancellationToken.None);

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
        IDomainEvent changed = what == nameof(OrderCollected) ? new OrderCollected(venueId) : new OrderAwaitingPayment(venueId);
        SpyNotifier board = new();
        SpyTill till = new();

        await new DomainEventDispatcher(board, till).DispatchAsync([changed], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(till.NotifiedVenues));
        Assert.Empty(board.NotifiedVenues);
    }

    [Fact]
    public async Task DispatchAsync_WhenTheBarsBoardChanges_LeavesTheTillAlone()
    {
        SpyTill till = new();

        await new DomainEventDispatcher(new SpyNotifier(), till)
            .DispatchAsync([new OrderQueued(Guid.CreateVersion7())], CancellationToken.None);

        Assert.Empty(till.NotifiedVenues);
    }

    // A future event this dispatcher does not yet know how to react to must
    // not throw: it is simply not its reaction to make, same as an unhandled
    // status in a switch that only some callers care about.
    [Fact]
    public async Task DispatchAsync_WhenTheEventIsNotOneItReactsTo_DoesNothing()
    {
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier, new SpyTill());

        await dispatcher.DispatchAsync([new SomeOtherEvent()], CancellationToken.None);

        Assert.Empty(notifier.NotifiedVenues);
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
}
