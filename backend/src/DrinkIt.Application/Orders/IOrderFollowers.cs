using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// Tells whoever is watching an order that it moved (US-22), so their
/// tracking screen asks again instead of waiting to be reloaded. Implemented
/// over SignalR in Infrastructure.
/// </summary>
/// <remarks>
/// Says only that something changed, never what: the screen asks the tracking
/// endpoint, which is where the link is checked and the venue is scoped. A
/// notification carrying the order would be a second way in to guard.
/// </remarks>
public interface IOrderFollowers
{
    Task NotifyOrderChangedAsync(TrackingToken order, CancellationToken cancellationToken);
}
