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
    public Task<bool> OverlapsAsync(DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excluding, CancellationToken cancellationToken) =>
        context.Nights.AnyAsync(
            night => night.Id != excluding && night.StartsAt < endsAt && startsAt < night.EndsAt,
            cancellationToken);

    /// <summary>Tracked, so the edit is saved. No venue in the WHERE: the global query filter adds it.</summary>
    public Task<Night?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        context.Nights.FirstOrDefaultAsync(night => night.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    public async Task AddAsync(Night night, CancellationToken cancellationToken)
    {
        context.Nights.Add(night);

        await context.SaveChangesAsync(cancellationToken);
    }
}
