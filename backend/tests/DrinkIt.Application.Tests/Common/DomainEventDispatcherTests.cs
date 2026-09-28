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
        DomainEventDispatcher dispatcher = new(notifier);

        await dispatcher.DispatchAsync([new OrderQueued(venueId)], CancellationToken.None);

        Assert.Equal(venueId, Assert.Single(notifier.NotifiedVenues));
    }

    // A future event this dispatcher does not yet know how to react to must
    // not throw: it is simply not its reaction to make, same as an unhandled
    // status in a switch that only some callers care about.
    [Fact]
    public async Task DispatchAsync_WhenTheEventIsNotOneItReactsTo_DoesNothing()
    {
        SpyNotifier notifier = new();
        DomainEventDispatcher dispatcher = new(notifier);

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
}
