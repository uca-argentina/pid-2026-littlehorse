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
                product.Stock,
                product.CategoryId,
                product.IsAvailable,
                // Product.IsSoldOut is not mapped, so the rule is restated for
                // SQL here. The domain test is the one that owns it.
                product.Stock == 0,
                product.IsActive,
                new AuditInfo(product.CreatedAt, product.CreatedBy, product.LastModifiedAt, product.LastModifiedBy)))
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// US-37: orderable is the switch on and some KDS of tonight holding at
    /// least one, not the venue's general stock: each bar sells only what was
    /// loaded into it for the night. With no night on, nothing is.
    /// </remarks>
    public async Task<IReadOnlyList<MenuItem>> ListForMenuAsync(Guid? nightId, CancellationToken cancellationToken) =>
        await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Select(product => new
            {
                Product = product,
                IsOrderable = product.IsAvailable
                    && context.BarStocks.Any(stock => stock.NightId == nightId && stock.ProductId == product.Id && stock.Units > 0),
            })
            // What can be ordered first, so the reachable part of the menu is
            // what a thumb lands on. Sold out stays visible, further down.
            .OrderByDescending(row => row.IsOrderable)
            .ThenBy(row => row.Product.Name)
            .Select(row => new MenuItem(
                row.Product.Id,
                row.Product.Name,
                row.Product.Description,
                row.Product.ImageUrl,
                row.Product.Price,
                row.Product.CategoryId,
                row.IsOrderable))
            .ToListAsync(cancellationToken);
}
