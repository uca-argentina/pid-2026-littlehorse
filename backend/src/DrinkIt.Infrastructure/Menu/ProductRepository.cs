using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
}
