using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Orders;

/// <summary>
/// The bar took it off the queue to make it (US-16). What moves its card to
/// En preparación on the venue's board.
/// </summary>
/// <remarks>
/// Written out longhand for the same reason as <see cref="OrderQueued"/>: a
/// positional record generates init setters, and the architecture tests refuse
/// a public setter anywhere in the domain.
/// </remarks>
public sealed record OrderPreparationStarted : IDomainEvent
{
    public OrderPreparationStarted(Guid venueId)
    {
        VenueId = venueId;
    }

    public Guid VenueId { get; }
}
