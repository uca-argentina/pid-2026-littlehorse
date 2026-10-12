using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DrinkIt.Infrastructure.Menu;

internal sealed class ProductRepository(DrinkItDbContext context) : IProductRepository
{
    /// <summary>
    /// No venue in the WHERE: the global query filter adds the one the request
    /// resolved. Writing it by hand is what would let one venue's check read
    /// another venue's rows. Case is ignored by the column's collation, which
    /// is what the unique index compares with too, so the two cannot disagree.
    /// </summary>
    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        context.Products.AnyAsync(product => product.Name == name, cancellationToken);

    public async Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        context.Products.Add(product);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Tracked, so the change lands with <see cref="SaveChangesAsync"/>. The
    /// venue filter applies here too: another venue's id finds nothing.
    /// </summary>
    public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        context.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// One conditional statement, the same shape as a sale in OrderRepository:
    /// the database adds the change to whatever it holds right now, so a sale
    /// between reading the product and this keeps its units. The night's record
    /// of it goes in the same transaction: both or neither. The venue filter
    /// applies here too.
    /// </summary>
    public async Task<bool> SaveStockAdjustmentAsync(Product product, NightStockAdjustment adjustment, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        int change = adjustment.Change;
        int applied = await context.Products
            .Where(row => row.Id == product.Id && row.Stock + change >= 0)
            .ExecuteUpdateAsync(row => row.SetProperty(p => p.Stock, p => p.Stock + change), cancellationToken);

        // Before saving anything tracked: what the domain worked out in memory
        // is not what is stored once sales are counted in, and saving it would
        // write the stale total over them. The screen gets what the database has.
        await context.Entry(product).ReloadAsync(cancellationToken);

        if (applied != 1)
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        context.NightStockAdjustments.Add(adjustment);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
