using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Nights;

/// <summary>Read side: no tracking, and a projection straight to the DTO. CLAUDE.md, persistence section.</summary>
internal sealed class NightQueries(DrinkItDbContext context) : INightQueries
{
    public async Task<IReadOnlyList<NightSummary>> ListAsync(CancellationToken cancellationToken) =>
        await context.Nights
            .AsNoTracking()
            // The next night, or the one on now, is what the administrator came for.
            .OrderByDescending(night => night.StartsAt)
            .Select(night => new NightSummary(
                night.Id,
                night.Name,
                night.StartsAt,
                night.EndsAt,
                // The crew is mapped to a private field (NightConfiguration).
                EF.Property<List<Guid>>(night, "_crewIds"),
                new AuditInfo(night.CreatedAt, night.CreatedBy, night.LastModifiedAt, night.LastModifiedBy)))
            .ToListAsync(cancellationToken);
}
