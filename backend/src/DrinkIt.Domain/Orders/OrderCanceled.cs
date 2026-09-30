namespace DrinkIt.Domain.Orders;

/// <summary>
/// Canceled while it waited to be paid at the till (US-23). The till's list
/// drops it, and the customer following it sees it say so.
/// </summary>
/// <remarks>
/// Written out longhand for the same reason as <see cref="OrderQueued"/>: a
/// positional record generates init setters, and the architecture tests refuse
/// a public setter anywhere in the domain.
/// </remarks>
public sealed record OrderCanceled : IOrderChanged
{
    public OrderCanceled(Guid venueId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public TrackingToken TrackingToken { get; }
}
