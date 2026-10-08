namespace DrinkIt.Domain.Orders;

/// <summary>
/// Marked ready by mistake and sent back to the bar (US-18). The board moves
/// its card back to En preparación.
/// </summary>
/// <remarks>
/// Written out longhand for the same reason as <see cref="OrderQueued"/>: a
/// positional record generates init setters, and the architecture tests refuse
/// a public setter anywhere in the domain.
/// </remarks>
public sealed record OrderReturnedToPreparation : IOrderChanged
{
    public OrderReturnedToPreparation(Guid venueId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public TrackingToken TrackingToken { get; }
}
