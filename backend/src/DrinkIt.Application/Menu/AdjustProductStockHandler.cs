using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>The units to add, or to take away when the stock was loaded wrong.</summary>
public sealed record AdjustProductStockCommand(Guid ProductId, int Change);

/// <summary>
/// Moves a product's general stock by hand: up whenever a delivery arrives,
/// down when it was loaded wrong. The change is what gets saved, never the
/// total the administrator saw: the bar keeps selling while the screen is
/// open, and a total typed over it would put back drinks already sold.
/// </summary>
/// <remarks>
/// US-37: what can be taken away is only what is in the storeroom. Units
/// loaded into the bars of a night not yet over are the bars' to sell; once
/// that night ends, whatever they did not sell is back in the storeroom.
/// </remarks>
public sealed class AdjustProductStockHandler(IProductRepository products, TimeProvider clock)
{
    public async Task<Result<ProductSummary>> HandleAsync(AdjustProductStockCommand command, CancellationToken cancellationToken)
    {
        Product? product = await products.GetForUpdateAsync(command.ProductId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;

        // The screen worked the change out from the stock it showed when it
        // opened. Sales since then leaving too little is expected, and gets the
        // same answer as a sale, or a load into a bar, landing between this
        // read and the write below.
        if (!product.CanAdjustStock(command.Change)) return ProductErrors.StockMoved;

        // What is left can only be a rule broken by a malformed request (a
        // change of zero): the domain throws and the API turns it into a 400.
        product.AdjustStock(command.Change);

        if (!await products.SaveStockAdjustmentAsync(product, command.Change, clock.GetUtcNow(), cancellationToken))
            return ProductErrors.StockMoved;

        return ProductSummary.Of(product);
    }
}
