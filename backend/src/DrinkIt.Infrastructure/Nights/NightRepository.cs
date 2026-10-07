using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Nights;

internal sealed class NightRepository(DrinkItDbContext context) : INightRepository
{
    /// <summary>
    /// Strict comparisons on both ends, so touching nights do not count. No
    /// venue in the WHERE: the global query filter adds it.
    /// </summary>
    public Task<bool> OverlapsAsync(DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken) =>
        context.Nights.AnyAsync(night => night.StartsAt < endsAt && startsAt < night.EndsAt, cancellationToken);

    public async Task AddAsync(Night night, CancellationToken cancellationToken)
    {
        context.Nights.Add(night);

        await context.SaveChangesAsync(cancellationToken);
    }
}
