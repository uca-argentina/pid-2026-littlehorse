using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>
/// The product as the administration screen shows it back after changing it —
/// creating it, or flipping its nightly switch.
/// </summary>
/// <remarks>
/// One record for every write and not one per use case: the screen draws the
/// same row whichever operation produced it, so a second copy of these fields
/// fields would only be a second thing to keep in step with the first. It stays
/// separate from <see cref="ProductListItem"/> because that one is the read
/// side and answers a query, not a command.
/// </remarks>
public sealed record ProductSummary(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    Guid CategoryId,
    bool IsAvailable,
    bool IsActive,
    AuditInfo Audit)
{
    internal static ProductSummary Of(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.ImageUrl,
        product.Price,
        product.CategoryId,
        product.IsAvailable,
        product.IsActive,
        AuditInfo.Of(product));
}
