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
    public async Task<IReadOnlyList<Product>> GetForOrderingAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await context.Products
            .Where(product => ids.Contains(product.Id))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Deliberately does nothing. What the handler changed is written by
    /// IOrderRepository.AddAsync, in the same unit of work as the order: saving
    /// here would commit sold stock before the order that sold it exists.
    /// </summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
