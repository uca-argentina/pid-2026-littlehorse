using DrinkIt.Application.Orders;
using DrinkIt.Domain.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Orders;

internal sealed class ProductsForOrdering(DrinkItDbContext context) : IProductsForOrdering
{
    /// <summary>
    /// Read-only, on purpose. These say what each drink is called, what it
    /// costs and whether there is any left; taking the stock down is done by
    /// IOrderRepository.AddAsync with a conditional statement, so nothing here
    /// is ever written back. The venue filter applies: an id this venue does
    /// not sell simply does not come back.
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetForOrderingAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await context.Products
            .AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .ToListAsync(cancellationToken);
}
