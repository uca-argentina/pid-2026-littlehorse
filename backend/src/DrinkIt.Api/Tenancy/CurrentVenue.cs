using DrinkIt.Application.Common;
using DrinkIt.Application.Venues;

namespace DrinkIt.Api.Tenancy;

/// <summary>
/// Holds the venue for one request. Registered scoped: the middleware resolves
/// it early and the DbContext, built with the same instance, reads it at query
/// time. That ordering is what avoids a circular dependency between the two.
/// </summary>
internal sealed class CurrentVenue : ICurrentVenue
{
    /// <summary>Empty until resolved, which makes every filtered query return nothing.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Who the venue is, not merely which one. Known only when a slug resolved
    /// it, because a token carries an id and no name. The customer's menu reads
    /// this to name the venue on screen; nothing else needs it.
    /// </summary>
    public VenueIdentity? Identity { get; private set; }

    /// <summary>From a token claim: an id, and nothing more about the venue.</summary>
    public void Resolve(Guid venueId) => Id = venueId;

    /// <summary>From the slug in the address, which is a whole row.</summary>
    public void Resolve(VenueIdentity venue)
    {
        Id = venue.Id;
        Identity = venue;
    }
}
