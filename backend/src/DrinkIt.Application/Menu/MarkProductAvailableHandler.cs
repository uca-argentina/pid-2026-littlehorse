using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

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
