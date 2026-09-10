using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Venues;

internal sealed class VenueLookup(DrinkItDbContext context) : IVenueLookup
{
    public Task<Guid?> FindIdBySlugAsync(string slug, CancellationToken cancellationToken) =>
        context.Venues
            .AsNoTracking()
            .Where(venue => venue.Slug == slug)
            .Select(venue => (Guid?)venue.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
