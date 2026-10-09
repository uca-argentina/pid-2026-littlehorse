using DrinkIt.Application.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Nights;

internal sealed class TonightsCrew(DrinkItDbContext context) : ITonightsCrew
{
    /// <summary>
    /// One round trip: the last night that started, and whether its crew holds
    /// the account, which SQL Server answers through OPENJSON on the crew
    /// column. No venue in the WHERE: the global query filter adds it.
    /// </summary>
    public Task<bool> IncludesAsync(Guid staffUserId, DateTimeOffset at, CancellationToken cancellationToken) =>
        context.Nights
            .AsNoTracking()
            .Where(night => night.StartsAt <= at)
            .OrderByDescending(night => night.StartsAt)
            .Select(night => EF.Property<List<Guid>>(night, "_crewIds").Contains(staffUserId))
            .FirstOrDefaultAsync(cancellationToken);
}
