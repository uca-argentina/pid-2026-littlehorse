using DrinkIt.Application.Common;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Nights;

/// <summary>The units to add to one product's stock of one night, or to take away when it was loaded wrong.</summary>
public sealed record AdjustNightStockCommand(Guid NightId, Guid ProductId, int Change);

/// <summary>What a product's stock of a night amounts to after an adjustment.</summary>
public sealed record NightStockFigures(Guid ProductId, int Loaded, int Sold, int Remaining)
{
    internal static NightStockFigures Of(NightStock stock) =>
        new(stock.ProductId, stock.Loaded, stock.Sold, stock.Remaining);
}

/// <summary>
/// US-37: moves what a night has of one product by hand. The change is what
/// gets saved, never the total the administrator saw: the bar keeps selling
/// while the screen is open, and a total typed over it would put back drinks
/// already sold.
/// </summary>
public sealed class AdjustNightStockHandler(INightStockRepository stocks)
{
    public async Task<Result<NightStockFigures>> HandleAsync(AdjustNightStockCommand command, CancellationToken cancellationToken)
    {
        NightStock? stock = await stocks.GetForUpdateAsync(command.NightId, command.ProductId, cancellationToken);

        if (stock is null) return NightErrors.StockNotFound;

        // The screen worked the change out from the stock it showed when it
        // opened. Sales since then leaving too little is expected, and gets the
        // same answer as a sale landing between this read and the write below.
        if (!stock.CanAdjust(command.Change)) return NightErrors.StockMoved;

        // What is left can only be a rule broken by a malformed request (a
        // change of zero): the domain throws and the API turns it into a 400.
        stock.Adjust(command.Change);

        if (!await stocks.SaveAdjustmentAsync(stock, command.Change, cancellationToken))
            return NightErrors.StockMoved;

        return NightStockFigures.Of(stock);
    }
}
