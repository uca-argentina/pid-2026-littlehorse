using DrinkIt.Application.Kds;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Common;

/// <summary>
/// Where "the handlers react" actually happens (CLAUDE.md's Domain Events +
/// Observer). For now every reaction is the same one — the board redraws
/// whenever an order changes column. Ready's push and the mozo's view are the
/// next ones to land here as their own branch, not as an `if` chain growing
/// somewhere a command handler cannot be trusted to remember.
/// </summary>
/// <remarks>
/// Lives here, not in Infrastructure, for the same reason
/// <see cref="DrinkIt.Application.Orders.DigitalPaymentStrategy"/> does: it has
/// no external dependency of its own, only other Application ports.
/// </remarks>
public sealed class DomainEventDispatcher(IKdsBoardNotifier kdsBoard) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        foreach (IDomainEvent domainEvent in events)
        {
            if (VenueWhoseBoardChanged(domainEvent) is Guid venueId) await kdsBoard.NotifyBoardChangedAsync(venueId, cancellationToken);
        }
    }

    /// <summary>The venue whose KDS board has to redraw, or null when this event moves no card.</summary>
    private static Guid? VenueWhoseBoardChanged(IDomainEvent domainEvent) => domainEvent switch
    {
        OrderQueued queued => queued.VenueId,
        OrderPreparationStarted taken => taken.VenueId,
        OrderRequeued requeued => requeued.VenueId,
        OrderReady ready => ready.VenueId,
        OrderReturnedToPreparation returned => returned.VenueId,
        OrderDelivered delivered => delivered.VenueId,
        OrderDeliveryUndone undone => undone.VenueId,
        _ => null,
    };
}
