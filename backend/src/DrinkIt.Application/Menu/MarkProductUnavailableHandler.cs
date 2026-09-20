using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

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
