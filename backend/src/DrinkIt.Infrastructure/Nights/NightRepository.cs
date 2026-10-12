using DrinkIt.Application.Nights;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    /// <summary>
    /// The night and its opening stock in one transaction. Every active
    /// product's row is locked first, as anything that checks the storeroom
    /// does, and then the storeroom is read, split and written. The night is
    /// new, so none of its bars has a row yet.
    /// </summary>
    public async Task AddAsync(Night night, IReadOnlyList<StaffUser> bars, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await Storeroom.LockActiveProductsAsync(context, night.VenueId, cancellationToken);

        List<Product> products = await context.Products.Where(product => product.IsActive).ToListAsync(cancellationToken);
        Dictionary<Guid, int> held = await Storeroom.HeldInBarsAsync(context, now, cancellationToken);

        IReadOnlyList<NightStockAdjustment> loads = night.LoadOpeningStock(
            bars,
            products.Select(product => (product, product.Stock - held.GetValueOrDefault(product.Id))),
            now);

        context.Nights.Add(night);
        context.BarStocks.AddRange(loads.Select(load => new BarStock
        {
            VenueId = load.VenueId,
            NightId = load.NightId,
            KdsId = load.KdsId,
            ProductId = load.ProductId,
            Units = load.Change,
        }));
        context.NightStockAdjustments.AddRange(loads);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>No venue in the WHERE: the global query filter adds it.</summary>
    public async Task<IReadOnlyCollection<Guid>> BarsHoldingStockAsync(Guid nightId, CancellationToken cancellationToken) =>
        await context.BarStocks
            .Where(stock => stock.NightId == nightId && stock.Units > 0)
            .Select(stock => stock.KdsId)
            .Distinct()
            .ToListAsync(cancellationToken);
}
