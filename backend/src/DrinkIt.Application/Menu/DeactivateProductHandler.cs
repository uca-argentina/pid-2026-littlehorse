using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>
/// US-08: takes a product off the menu for good. The row stays — old orders
/// keep pointing at it — and <see cref="IProductQueries.ListForMenuAsync"/>
/// already excludes it from what the customer sees, so there is nothing
/// further to teach that query.
/// </summary>
public sealed class DeactivateProductHandler(IProductRepository products)
{
    public async Task<Result<ProductSummary>> HandleAsync(Guid productId, CancellationToken cancellationToken)
    {
        Product? product = await products.GetForUpdateAsync(productId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;

        product.Deactivate();

        await products.SaveChangesAsync(cancellationToken);

        return ProductSummary.Of(product);
    }
}
