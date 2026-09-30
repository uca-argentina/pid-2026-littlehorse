using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Venues;

/// <summary>Venues carry no global query filter, so this sees them all — ids only.</summary>
internal sealed class VenueDirectory(DrinkItDbContext context) : IVenueDirectory
{
    public async Task<IReadOnlyList<Guid>> AllIdsAsync(CancellationToken cancellationToken) =>
        await context.Venues.AsNoTracking().Select(venue => venue.Id).ToListAsync(cancellationToken);
}
