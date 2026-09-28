using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Orders;

/// <summary>
/// Taken by mistake and handed back (US-16, criterion 4): back among Nuevos,
/// still as old as the moment it was paid.
/// </summary>
/// <remarks>
/// Written out longhand for the same reason as <see cref="OrderQueued"/>: a
/// positional record generates init setters, and the architecture tests refuse
/// a public setter anywhere in the domain.
/// </remarks>
public sealed record OrderRequeued : IDomainEvent
{
    public OrderRequeued(Guid venueId)
    {
        VenueId = venueId;
    }

    public Guid VenueId { get; }
}
