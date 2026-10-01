namespace DrinkIt.Domain.Orders;

/// <summary>
/// An order was confirmed to be paid in cash (US-24): what the till reacts to,
/// so it shows up in "Por cobrar" without anybody reloading. The bar does not
/// care: nothing is to be made yet.
/// </summary>
public sealed record OrderAwaitingPayment : IOrderChanged
{
    public OrderAwaitingPayment(Guid venueId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public TrackingToken TrackingToken { get; }
}
