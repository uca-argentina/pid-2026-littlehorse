using DrinkIt.Application.Common;

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

    public void Resolve(Guid venueId) => Id = venueId;
}
