using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Venues;

internal sealed class VenueLookup(DrinkItDbContext context) : IVenueLookup
{
    public Task<VenueIdentity?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
        context.Venues
            .AsNoTracking()
            .Where(venue => venue.Slug == slug)
            .Select(venue => new VenueIdentity(venue.Id, venue.Name, venue.Slug))
            .FirstOrDefaultAsync(cancellationToken);
}
