using DrinkIt.Application.Common;
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
                product.CategoryId,
                product.IsAvailable,
                product.IsActive,
                new AuditInfo(product.CreatedAt, product.CreatedBy, product.LastModifiedAt, product.LastModifiedBy)))
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// What is left of a product is what the night has (US-37), never the
    /// number on the product. With no night on there is nothing to be sold out
    /// of, so only the nightly switch counts; and with one on, a product the
    /// night has no row for is not on its shelf.
    /// </remarks>
    public async Task<IReadOnlyList<MenuItem>> ListForMenuAsync(Guid? night, CancellationToken cancellationToken)
    {
        bool hasNight = night is not null;
        Guid nightId = night ?? Guid.Empty;

        // What can be ordered comes first, so the reachable part of the menu is
        // what a thumb lands on; sold out stays visible, further down. The
        // rule is restated in SQL because it cannot cross from the domain, and
        // MenuQueriesTests is the one that owns it.
        return await (
            from product in context.Products.AsNoTracking()
            where product.IsActive
            join stock in context.NightStocks.AsNoTracking().Where(row => row.NightId == nightId)
                on product.Id equals stock.ProductId into stocks
            from stock in stocks.DefaultIfEmpty()
            orderby (product.IsAvailable && (!hasNight || stock.Remaining > 0)) descending, product.Name
            select new MenuItem(
                product.Id,
                product.Name,
                product.Description,
                product.ImageUrl,
                product.Price,
                product.CategoryId,
                product.IsAvailable && (!hasNight || stock.Remaining > 0)))
            .ToListAsync(cancellationToken);
    }
}
