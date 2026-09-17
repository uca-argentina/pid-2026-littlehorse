using DrinkIt.Application.Orders;
using DrinkIt.Domain.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Orders;

internal sealed class ProductsForOrdering(DrinkItDbContext context) : IProductsForOrdering
{
    /// <summary>
    /// Tracked, because these are about to be sold from. The venue filter
    /// applies: an id this venue does not sell simply does not come back.
    /// </summary>
    /// <remarks>
    /// What is taken from these is written by IOrderRepository.AddAsync, which
    /// shares this context: one save, one transaction, order and stock
    /// together. Stock is a concurrency token, so a product somebody else sold
    /// from in between refuses the write instead of overwriting their count.
    /// </remarks>
    public async Task<IReadOnlyList<Product>> GetForOrderingAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await context.Products
            .Where(product => ids.Contains(product.Id))
            .ToListAsync(cancellationToken);
}
