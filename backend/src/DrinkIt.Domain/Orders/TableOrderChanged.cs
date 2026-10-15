namespace DrinkIt.Domain.Orders;

/// <summary>
/// What a waiter sees of a table's order changed (US-43): it became ready, was
/// taken, went back to the bar or was delivered. Every waiter of the venue is
/// looking at the same list, so every one of them redraws.
/// </summary>
/// <remarks>
/// Raised next to the order's own event and only for orders carried to a table:
/// the bar's own orders are no waiter's business. Written out longhand for the
/// same reason as <see cref="OrderQueued"/>: a positional record generates init
/// setters, and the architecture tests refuse a public setter in the domain.
/// </remarks>
public sealed record TableOrderChanged : IOrderChanged
{
    public TableOrderChanged(Guid venueId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public TrackingToken TrackingToken { get; }
}
