using DrinkIt.Application.Cashier;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Common;

/// <summary>
/// Where "the handlers react" actually happens (CLAUDE.md's Domain Events +
/// Observer). Three reactions so far: the bar's board redraws when an order
/// changes column, the till's list when what waits for cash changes, and
/// whoever follows an order hears of every move it makes (US-22). Ready's push
/// and the mozo's view are the next ones to land here as their own branch, not
/// as an `if` chain growing somewhere a command handler cannot be trusted to
/// remember.
/// </summary>
/// <remarks>
/// Lives here, not in Infrastructure, for the same reason
/// <see cref="DrinkIt.Application.Orders.DigitalPaymentStrategy"/> does: it has
/// no external dependency of its own, only other Application ports.
/// </remarks>
public sealed class DomainEventDispatcher(IKdsBoardNotifier kdsBoard, ITillNotifier till, IOrderFollowers followers) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        foreach (IDomainEvent domainEvent in events)
        {
            if (VenueWhoseBoardChanged(domainEvent) is Guid venueId) await kdsBoard.NotifyBoardChangedAsync(venueId, cancellationToken);

            if (VenueWhoseTillChanged(domainEvent) is Guid tillVenueId) await till.NotifyTillChangedAsync(tillVenueId, cancellationToken);
        }

        // Once per order, not once per event: one move can raise several
        // (collecting cash queues it too), and each notification is a phone
        // asking the API again for the same answer.
        foreach (TrackingToken order in events.OfType<IOrderChanged>().Select(changed => changed.TrackingToken).Distinct())
        {
            await followers.NotifyOrderChangedAsync(order, cancellationToken);
        }
    }

    /// <summary>
    /// The venue whose tills have to reload "Por cobrar", or null when nothing
    /// waits for cash differently. A cancellation is one (US-23): only an order
    /// waiting for cash can be canceled, and it leaves the list.
    /// </summary>
    private static Guid? VenueWhoseTillChanged(IDomainEvent domainEvent) => domainEvent switch
    {
        OrderAwaitingPayment waiting => waiting.VenueId,
        OrderCollected collected => collected.VenueId,
        OrderCanceled canceled => canceled.VenueId,
        _ => null,
    };

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
