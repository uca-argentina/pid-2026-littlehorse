using DrinkIt.Application.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Nights;

internal sealed class UnderwayNightLookup(DrinkItDbContext context) : IUnderwayNightLookup
{
    /// <summary>
    /// The same rule as Night.IsUnderwayAt, written as SQL. At most one night
    /// matches, because a venue's nights never overlap. No venue in the WHERE:
    /// the global query filter adds it.
    /// </summary>
    public Task<Guid?> FindIdAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        context.Nights
            .AsNoTracking()
            .Where(night => night.StartsAt <= at && at < night.EndsAt)
            .Select(night => (Guid?)night.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
