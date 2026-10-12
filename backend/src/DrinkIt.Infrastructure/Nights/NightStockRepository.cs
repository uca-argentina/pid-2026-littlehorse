using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DrinkIt.Infrastructure.Nights;

internal sealed class NightStockRepository(DrinkItDbContext context) : INightStockRepository
{
    /// <summary>
    /// Every active product's row locked first, as anything that checks the
    /// storeroom does, then the storeroom read, split and written: the bars'
    /// rows and the record of each load, in one transaction. The night is new,
    /// so none of its bars has a row yet.
    /// </summary>
    public async Task LoadOpeningStockAsync(Night night, IReadOnlyList<StaffUser> bars, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await Storeroom.LockActiveProductsAsync(context, night.VenueId, cancellationToken);

        List<Product> products = await context.Products.Where(product => product.IsActive).ToListAsync(cancellationToken);
        Dictionary<Guid, int> held = await Storeroom.HeldInBarsAsync(context, now, cancellationToken);

        IReadOnlyList<NightStockAdjustment> loads = night.LoadOpeningStock(
            bars,
            products.Select(product => (product, product.Stock - held.GetValueOrDefault(product.Id))),
            now);

        foreach (NightStockAdjustment load in loads)
        {
            context.BarStocks.Add(new BarStock
            {
                VenueId = load.VenueId,
                NightId = load.NightId,
                KdsId = load.KdsId,
                ProductId = load.ProductId,
                Units = load.Change,
            });
        }

        context.NightStockAdjustments.AddRange(loads);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The bar's units and the night's record, in one transaction. The general
    /// stock is not touched: loading moves units from the storeroom into the
    /// bar, and taking them out moves them back. The venue filter applies to
    /// every statement but the lock.
    /// </summary>
    /// <remarks>
    /// The product's row is locked first, so nothing else that writes its stock
    /// gets in between the storeroom's count and the write. The bar moves with
    /// a conditional statement, the same shape as a sale in OrderRepository, so
    /// a sale is kept without needing that lock.
    /// </remarks>
    public async Task<Result<NightStockAdjustment>> SaveAdjustmentAsync(NightStockAdjustment adjustment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        int change = adjustment.Change;
        int stock = await Storeroom.LockProductAsync(context, adjustment.ProductId, cancellationToken);

        if (change > 0 && stock - await Storeroom.HeldInBarsAsync(context, adjustment.ProductId, now, cancellationToken) < change)
        {
            await transaction.RollbackAsync(cancellationToken);

            return NightErrors.StoreroomShort;
        }

        int applied = await context.BarStocks
            .Where(row => row.NightId == adjustment.NightId && row.KdsId == adjustment.KdsId && row.ProductId == adjustment.ProductId
                && row.Units + change >= 0)
            .ExecuteUpdateAsync(row => row.SetProperty(bar => bar.Units, bar => bar.Units + change), cancellationToken);

        if (applied == 0)
        {
            // Taking out: the bar holds less than that, or never held any.
            if (change < 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                return ProductErrors.StockMoved;
            }

            // Loading: the bar's first units of it this night. No two first
            // loads race to insert, because both wait on the product's lock.
            context.BarStocks.Add(new BarStock
            {
                VenueId = adjustment.VenueId,
                NightId = adjustment.NightId,
                KdsId = adjustment.KdsId,
                ProductId = adjustment.ProductId,
                Units = change,
            });
        }

        context.NightStockAdjustments.Add(adjustment);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return adjustment;
    }
}
