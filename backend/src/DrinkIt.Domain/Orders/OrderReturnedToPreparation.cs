using DrinkIt.Domain.Common;

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
public sealed record OrderReturnedToPreparation : IDomainEvent
{
    public OrderReturnedToPreparation(Guid venueId)
    {
        VenueId = venueId;
    }

    public Guid VenueId { get; }
}
