namespace DrinkIt.Domain.Orders;

/// <summary>
/// A delivery undone within its grace (US-18): the order is Ready again, back
/// in Listos en la barra.
/// </summary>
/// <remarks>
/// Written out longhand for the same reason as <see cref="OrderQueued"/>: a
/// positional record generates init setters, and the architecture tests refuse
/// a public setter anywhere in the domain.
/// </remarks>
public sealed record OrderDeliveryUndone : IOrderChanged
{
    public OrderDeliveryUndone(Guid venueId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public TrackingToken TrackingToken { get; }
}
