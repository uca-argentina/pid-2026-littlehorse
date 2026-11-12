namespace DrinkIt.Domain.Orders;

public sealed record OrderRestarted : IOrderChanged
{
    public OrderRestarted(Guid venueId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public TrackingToken TrackingToken { get; }
}
