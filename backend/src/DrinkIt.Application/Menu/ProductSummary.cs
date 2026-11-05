using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>
/// The product as the administration screen shows it back after changing it —
/// creating it, or flipping its nightly switch.
/// </summary>
/// <remarks>
/// One record for every write and not one per use case: the screen draws the
/// same row whichever operation produced it, so a second copy of these nine
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
    int Stock,
    Guid CategoryId,
    bool IsAvailable,
    bool IsSoldOut,
    bool IsActive,
    AuditInfo Audit,
    IReadOnlyList<ComboPart>? Parts = null)
{
    /// <summary>What it is made of when it is a combo (US-50). Empty otherwise.</summary>
    public IReadOnlyList<ComboPart> Components => Parts ?? [];

    public bool IsCombo => Components.Count > 0;

    internal static ProductSummary Of(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.ImageUrl,
        product.Price,
        product.Stock,
        product.CategoryId,
        product.IsAvailable,
        product.IsSoldOut,
        product.IsActive,
        AuditInfo.Of(product),
        [.. product.Components.Select(component => new ComboPart(component.ProductId, component.Quantity))]);
}
