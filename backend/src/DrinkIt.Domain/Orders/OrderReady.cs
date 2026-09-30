namespace DrinkIt.Domain.Orders;

/// <summary>
/// The drink is made (US-18). The board moves the card to Listos en la barra,
/// and the order is named because the customer's push (US-21) is about this
/// order in particular, not about the venue's board.
/// </summary>
/// <remarks>
/// Written out longhand for the same reason as <see cref="OrderQueued"/>: a
/// positional record generates init setters, and the architecture tests refuse
/// a public setter anywhere in the domain.
/// </remarks>
public sealed record OrderReady : IOrderChanged
{
    public OrderReady(Guid venueId, Guid orderId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        OrderId = orderId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public Guid OrderId { get; }

    public TrackingToken TrackingToken { get; }
}
