using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Nights;

/// <summary>
/// No venue in any WHERE below: the global query filter adds the one the
/// request resolved, so a night, a product or a row of another venue is simply
/// not here.
/// </summary>
internal sealed class NightStockRepository(DrinkItDbContext context) : INightStockRepository
{
    public async Task<NightForStock?> FindNightAsync(Guid nightId, CancellationToken cancellationToken)
    {
        var night = await context.Nights
            .AsNoTracking()
            .Where(candidate => candidate.Id == nightId)
            .Select(candidate => new { candidate.Id, candidate.StartsAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (night is null) return null;

        // Nights never overlap, so the latest one that started before this one
        // is also the latest one that ended before it.
        DateTimeOffset? previousEnd = await context.Nights
            .AsNoTracking()
            .Where(candidate => candidate.StartsAt < night.StartsAt)
            .OrderByDescending(candidate => candidate.StartsAt)
            .Select(candidate => (DateTimeOffset?)candidate.EndsAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new NightForStock(night.Id, night.StartsAt, previousEnd);
    }

    /// <summary>Tracked: the rows are what an adjustment changes.</summary>
    public async Task<IReadOnlyList<NightStock>> ListAsync(Guid nightId, CancellationToken cancellationToken) =>
        await context.NightStocks
            .Where(stock => stock.NightId == nightId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockedProduct>> ListActiveProductsAsync(CancellationToken cancellationToken) =>
        await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new StockedProduct(product.Id, product.Name, product.InitialStock))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CarriedOverAsync(Guid nightId, CancellationToken cancellationToken)
    {
        DateTimeOffset? startsAt = await context.Nights
            .AsNoTracking()
            .Where(candidate => candidate.Id == nightId)
            .Select(candidate => (DateTimeOffset?)candidate.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (startsAt is null) return new Dictionary<Guid, int>();

        // The latest earlier night that has a row for each product. A night
        // that was never opened simply has none, and the one before it is the
        // one that counts.
        var latest = await (
            from stock in context.NightStocks.AsNoTracking()
            join night in context.Nights.AsNoTracking() on stock.NightId equals night.Id
            where night.StartsAt < startsAt
            select new { stock.ProductId, night.StartsAt, stock.Remaining })
            .GroupBy(row => row.ProductId)
            .Select(group => group.OrderByDescending(row => row.StartsAt).First())
            .ToListAsync(cancellationToken);

        return latest.ToDictionary(row => row.ProductId, row => row.Remaining);
    }

    /// <summary>Tracked, so the audit stamp of the adjustment can be saved.</summary>
    public Task<NightStock?> GetForUpdateAsync(Guid nightId, Guid productId, CancellationToken cancellationToken) =>
        context.NightStocks.FirstOrDefaultAsync(
            stock => stock.NightId == nightId && stock.ProductId == productId,
            cancellationToken);

    /// <summary>
    /// One conditional statement, the same shape as a sale in OrderRepository:
    /// the database adds the change to whatever it holds right now, so a sale
    /// between reading the row and this keeps its units. The venue filter
    /// applies here too.
    /// </summary>
    public async Task<bool> SaveAdjustmentAsync(NightStock stock, int change, CancellationToken cancellationToken)
    {
        int applied = await context.NightStocks
            .Where(row => row.Id == stock.Id && row.Remaining + change >= 0)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.Loaded, row => row.Loaded + change)
                    .SetProperty(row => row.Remaining, row => row.Remaining + change),
                cancellationToken);

        // What the domain worked out in memory is not what is stored once sales
        // are counted in: the screen gets what the database has.
        await context.Entry(stock).ReloadAsync(cancellationToken);

        if (applied != 1) return false;

        // The statement above is not a tracked save, so it leaves no mark of
        // who did it. Flagging the stamp as changed makes the interceptor write
        // it, and only it: the figures just reloaded are not written back.
        context.Entry(stock).Property(nameof(NightStock.LastModifiedAt)).IsModified = true;
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task AddRangeAsync(IReadOnlyCollection<NightStock> rows, CancellationToken cancellationToken)
    {
        context.NightStocks.AddRange(rows);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique index refused a row: another request opened the same
            // night in the meantime. Whatever is already there stands, and only
            // what is still missing is written.
            context.ChangeTracker.Clear();

            Guid nightId = rows.First().NightId;
            HashSet<Guid> present = [.. await context.NightStocks
                .AsNoTracking()
                .Where(stock => stock.NightId == nightId)
                .Select(stock => stock.ProductId)
                .ToListAsync(cancellationToken)];

            context.NightStocks.AddRange(rows.Where(row => !present.Contains(row.ProductId)));

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
