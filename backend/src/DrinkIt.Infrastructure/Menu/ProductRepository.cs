using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
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
    /// A conditional statement, the same shape as a sale in OrderRepository:
    /// the database adds the change to whatever it holds right now, so a sale
    /// between reading the product and this keeps its units. Only taking away
    /// asks about the storeroom (US-37), and with the product's row locked, so
    /// nothing is loaded into a bar between that count and the write. The
    /// venue filter applies to every statement but the lock.
    /// </summary>
    public async Task<bool> SaveStockAdjustmentAsync(Product product, int change, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await Storeroom.LockProductAsync(context, product.Id, cancellationToken);

        int held = change < 0 ? await Storeroom.HeldInBarsAsync(context, product.Id, now, cancellationToken) : 0;

        int applied = await context.Products
            .Where(row => row.Id == product.Id && row.Stock + change >= held)
            .ExecuteUpdateAsync(row => row.SetProperty(p => p.Stock, p => p.Stock + change), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // What the domain worked out in memory is not what is stored once sales
        // are counted in: the screen gets the stock the database has.
        await context.Entry(product).ReloadAsync(cancellationToken);

        return applied == 1;
    }
}
