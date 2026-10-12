using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DrinkIt.Infrastructure.Nights;

internal sealed class NightStockRepository(DrinkItDbContext context) : INightStockRepository
{
    /// <summary>
    /// The bar's units, the product's total and the night's record, in one
    /// transaction. The bar moves with a conditional statement, the same shape
    /// as a sale in OrderRepository: the database adds the change to whatever
    /// it holds right now. The venue filter applies to every statement here.
    /// </summary>
    /// <remarks>
    /// A bar's first load of a drink has no row to add to, so it inserts one.
    /// Two first loads at once both try; the key lets one through, and the
    /// other goes round once more and finds the row.
    /// </remarks>
    public async Task<bool> SaveAdjustmentAsync(NightStockAdjustment adjustment, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await TrySaving(adjustment, cancellationToken);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                context.ChangeTracker.Clear();
            }
        }
    }

    private async Task<bool> TrySaving(NightStockAdjustment adjustment, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        int change = adjustment.Change;
        int applied = await context.BarStocks
            .Where(stock => stock.KdsId == adjustment.KdsId && stock.ProductId == adjustment.ProductId && stock.Units + change >= 0)
            .ExecuteUpdateAsync(row => row.SetProperty(stock => stock.Units, stock => stock.Units + change), cancellationToken);

        if (applied == 0)
        {
            // No row matched: the bar holds less than the change takes away,
            // or has never held any of it. Only a load can start one.
            bool exists = await context.BarStocks
                .AnyAsync(stock => stock.KdsId == adjustment.KdsId && stock.ProductId == adjustment.ProductId, cancellationToken);

            if (exists || change < 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                return false;
            }

            context.BarStocks.Add(new BarStock
            {
                VenueId = adjustment.VenueId,
                KdsId = adjustment.KdsId,
                ProductId = adjustment.ProductId,
                Units = change,
            });
        }

        await context.Products
            .Where(product => product.Id == adjustment.ProductId)
            .ExecuteUpdateAsync(row => row.SetProperty(product => product.Stock, product => product.Stock + change), cancellationToken);

        context.NightStockAdjustments.Add(adjustment);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
