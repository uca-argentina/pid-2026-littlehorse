using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

public sealed record RestockProductCommand(Guid ProductId, int Units);

/// <summary>
/// Stock arrives at the bar. The units are added to what was left, so
/// restocking a drink that is still selling cannot overwrite the sales made a
/// moment earlier with a stale total the administrator saw on the screen.
/// </summary>
/// <remarks>
/// The read-then-save is not atomic against a sale in the same few
/// milliseconds. The case restocking exists for is a drink that ran out, and
/// nothing can be sold from zero, so there is nothing to race. Stock stays out
/// of the concurrency check on purpose — see ProductConfiguration.
/// </remarks>
public sealed class RestockProductHandler(IProductRepository products)
{
    public async Task<Result<ProductSummary>> HandleAsync(RestockProductCommand command, CancellationToken cancellationToken)
    {
        Product? product = await products.GetForUpdateAsync(command.ProductId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;

        // Units that are not positive are a broken domain rule, and Restock
        // throws. The API turns that into a 400.
        product.Restock(command.Units);

        await products.SaveChangesAsync(cancellationToken);

        return ProductSummary.Of(product);
    }
}
