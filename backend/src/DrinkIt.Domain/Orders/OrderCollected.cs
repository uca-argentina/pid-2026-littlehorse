namespace DrinkIt.Domain.Orders;

/// <summary>
/// A till took the money for an order (US-26): every till of the venue drops
/// it from "Por cobrar". The bar hears about it through <see cref="OrderQueued"/>.
/// </summary>
public sealed record OrderCollected : IOrderChanged
{
    public OrderCollected(Guid venueId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public TrackingToken TrackingToken { get; }
}
