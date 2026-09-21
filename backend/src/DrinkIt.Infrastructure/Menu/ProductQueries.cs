using DrinkIt.Application.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Menu;

/// <summary>
/// Read side: no repository, no tracking, and a projection straight to the DTO
/// the screen shows. CLAUDE.md, persistence section.
/// </summary>
internal sealed class ProductQueries(DrinkItDbContext context) : IProductQueries
{
    public async Task<IReadOnlyList<ProductListItem>> ListAsync(CancellationToken cancellationToken) =>
        await context.Products
            .AsNoTracking()
            // What is still on sale comes first: the deactivated rows are kept
            // for the order history, not to be read every shift.
            .OrderByDescending(product => product.IsActive)
            .ThenBy(product => product.Name)
            .Select(product => new ProductListItem(
                product.Id,
                product.Name,
                product.Description,
                product.ImageUrl,
                product.Price,
                product.Stock,
                product.IsAvailable,
                // Product.IsSoldOut is not mapped, so the rule is restated for
                // SQL here. The domain test is the one that owns it.
                product.Stock == 0,
                product.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MenuItem>> ListForMenuAsync(CancellationToken cancellationToken) =>
        await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            // What can be ordered first, so the reachable part of the menu is
            // what a thumb lands on. Sold out stays visible, further down.
            //
            // Product.IsOrderable is the same rule and owns the test for it,
            // but it is not a mapped column, so it cannot cross into SQL and is
            // restated here. Ordering after projecting to MenuItem would read
            // better (sort on the already-computed IsOrderable instead of
            // repeating the expression), but EF Core cannot translate an
            // OrderBy over a property read back off a constructed record — it
            // has to be a SQL ORDER BY over the raw columns, so the condition
            // is written twice.
            .OrderByDescending(product => product.IsAvailable && product.Stock > 0)
            .ThenBy(product => product.Name)
            .Select(product => new MenuItem(
                product.Id,
                product.Name,
                product.Description,
                product.ImageUrl,
                product.Price,
                product.IsAvailable && product.Stock > 0))
            .ToListAsync(cancellationToken);
}
