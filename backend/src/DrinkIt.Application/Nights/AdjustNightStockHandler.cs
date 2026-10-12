using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Nights;

/// <summary>The units to load into the night, or to take out of it when they were loaded wrong.</summary>
public sealed record AdjustNightStockCommand(Guid NightId, Guid ProductId, int Change);

/// <summary>
/// US-37: the administrator loads the night's stock, product by product. The
/// change is what gets saved, never the total on the screen: the bar keeps
/// selling while it is open, and a total typed over it would put back drinks
/// already sold.
/// </summary>
public sealed class AdjustNightStockHandler(
    INightRepository nights,
    IProductRepository products,
    INightStockQueries sheets,
    TimeProvider clock)
{
    public async Task<Result<NightStockSheet>> HandleAsync(AdjustNightStockCommand command, CancellationToken cancellationToken)
    {
        Night? night = await nights.GetForUpdateAsync(command.NightId, cancellationToken);

        if (night is null) return NightErrors.NotFound;

        DateTimeOffset now = clock.GetUtcNow();

        // Both expected from a screen that was opened a while ago, so results
        // and not the domain's exception.
        if (night.HasEndedAt(now)) return NightErrors.StockClosed;
        if (await nights.HasUnfinishedBeforeAsync(night.StartsAt, now, cancellationToken)) return NightErrors.EarlierNightOpen;

        Product? product = await products.GetForUpdateAsync(command.ProductId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;

        // Sales since the screen opened leaving too little is expected, and gets
        // the same answer as a sale landing between this read and the write.
        if (!product.CanAdjustStock(command.Change)) return ProductErrors.StockMoved;

        // What is left can only be a malformed request (a change of zero): the
        // domain throws and the API turns it into a 400.
        NightStockAdjustment adjustment = night.AdjustStock(product, command.Change, now);

        if (!await products.SaveStockAdjustmentAsync(product, adjustment, cancellationToken)) return ProductErrors.StockMoved;

        return (await sheets.GetAsync(night.Id, cancellationToken))!;
    }
}
