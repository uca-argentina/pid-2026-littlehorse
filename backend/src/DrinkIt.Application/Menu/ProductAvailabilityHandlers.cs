using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>The product as any administration screen that changed it shows it back.</summary>
public sealed record ProductSummary(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    int Stock,
    bool IsAvailable,
    bool IsSoldOut,
    bool IsActive)
{
    internal static ProductSummary Of(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.ImageUrl,
        product.Price,
        product.Stock,
        product.IsAvailable,
        product.IsSoldOut,
        product.IsActive);
}

/// <summary>
/// US-07: turns a product's nightly switch off, e.g. the ice machine broke.
/// The customer keeps seeing it, dimmed and without the button to add it —
/// disappearing would read as a loading bug, per the story's design note.
/// </summary>
public sealed class MarkProductUnavailableHandler(IProductRepository products)
{
    public async Task<Result<ProductSummary>> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        Product? product = await products.GetForUpdateAsync(productId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;

        product.MarkUnavailable();

        await products.SaveChangesAsync(cancellationToken);

        return ProductSummary.Of(product);
    }
}

/// <summary>
/// US-07: turns the switch back on, e.g. after restocking. Does not touch
/// stock, so a product at zero stays sold out until it is restocked too.
/// </summary>
public sealed class MarkProductAvailableHandler(IProductRepository products)
{
    public async Task<Result<ProductSummary>> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        Product? product = await products.GetForUpdateAsync(productId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;

        product.MarkAvailable();

        await products.SaveChangesAsync(cancellationToken);

        return ProductSummary.Of(product);
    }
}
