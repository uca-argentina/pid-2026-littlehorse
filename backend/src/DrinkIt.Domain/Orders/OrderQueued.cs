namespace DrinkIt.Domain.Orders;

/// <summary>
/// An order reached the bar's queue (US-15). What the KDS board's tablet
/// reacts to so a new order shows up without anybody reloading by hand.
/// </summary>
/// <remarks>
/// Written out longhand rather than as a positional record, same as
/// <see cref="NewOrderItem"/>: those generate init setters, and
/// DrinkIt.ArchitectureTests refuses a public setter anywhere in the domain.
/// </remarks>
public sealed record OrderQueued : IOrderChanged
{
    public OrderQueued(Guid venueId, TrackingToken trackingToken)
    {
        VenueId = venueId;
        TrackingToken = trackingToken;
    }

    public Guid VenueId { get; }

    public TrackingToken TrackingToken { get; }
}
