using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Orders;

/// <summary>
/// Handed over at the bar (US-18's manual fallback, US-19). The card leaves the
/// board.
/// </summary>
/// <remarks>
/// Written out longhand for the same reason as <see cref="OrderQueued"/>: a
/// positional record generates init setters, and the architecture tests refuse
/// a public setter anywhere in the domain.
/// </remarks>
public sealed record OrderDelivered : IDomainEvent
{
    public OrderDelivered(Guid venueId)
    {
        VenueId = venueId;
    }

    public Guid VenueId { get; }
}
