using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Tests.Nights;

/// <summary>
/// US-37, criterion 1: the administrator adds or takes away units of a
/// product on the night's own stock. The change is what is saved, never the
/// total the screen showed: the bar keeps selling while it is open.
/// </summary>
public class AdjustNightStockHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();
    private static readonly Guid ANight = Guid.CreateVersion7();
    private static readonly Guid AProduct = Guid.CreateVersion7();

    [Fact]
    public async Task HandleAsync_WhenUnitsArrive_AddsThemAndReturnsTheFigures()
    {
        (NightStocksInMemory stocks, _) = AStockWith(loaded: 12);

        Result<NightStockFigures> result = await new AdjustNightStockHandler(stocks)
            .HandleAsync(new AdjustNightStockCommand(ANight, AProduct, 8), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((AProduct, 20, 0, 20), (result.Value.ProductId, result.Value.Loaded, result.Value.Sold, result.Value.Remaining));
        Assert.Equal(1, stocks.AdjustmentsSaved);
    }

    [Fact]
    public async Task HandleAsync_WhenItTakesAwayWhatWasLoadedByMistake_LowersTheStock()
    {
        (NightStocksInMemory stocks, _) = AStockWith(loaded: 200);

        Result<NightStockFigures> result = await new AdjustNightStockHandler(stocks)
            .HandleAsync(new AdjustNightStockCommand(ANight, AProduct, -180), CancellationToken.None);

        Assert.Equal(20, result.Value.Remaining);
    }

    // The night never opened that product, or the id is not this venue's:
    // from the screen the two are the same thing.
    [Fact]
    public async Task HandleAsync_WhenTheNightHasNoRowForTheProduct_FailsWithStockNotFound()
    {
        (NightStocksInMemory stocks, _) = AStockWith(loaded: 12);

        Result<NightStockFigures> result = await new AdjustNightStockHandler(stocks)
            .HandleAsync(new AdjustNightStockCommand(ANight, Guid.CreateVersion7(), 3), CancellationToken.None);

        Assert.Equal(NightErrors.StockNotFound, result.Error);
        Assert.Equal(0, stocks.AdjustmentsSaved);
    }

    // The screen worked the change out from what it showed when it opened.
    // Sales since leaving less is something that happens, not a broken rule.
    [Fact]
    public async Task HandleAsync_WhenItWouldGoBelowZero_FailsWithStockMovedWithoutSaving()
    {
        (NightStocksInMemory stocks, _) = AStockWith(loaded: 5);

        Result<NightStockFigures> result = await new AdjustNightStockHandler(stocks)
            .HandleAsync(new AdjustNightStockCommand(ANight, AProduct, -6), CancellationToken.None);

        Assert.Equal(NightErrors.StockMoved, result.Error);
        Assert.Equal(0, stocks.AdjustmentsSaved);
    }

    // The same answer as above, one step later: it fit when read, and a sale
    // landed before the write.
    [Fact]
    public async Task HandleAsync_WhenASaleLandedBeforeTheWrite_FailsWithStockMoved()
    {
        (NightStocksInMemory stocks, _) = AStockWith(loaded: 10);
        stocks.RefusesTheAdjustment = true;

        Result<NightStockFigures> result = await new AdjustNightStockHandler(stocks)
            .HandleAsync(new AdjustNightStockCommand(ANight, AProduct, -4), CancellationToken.None);

        Assert.Equal(NightErrors.StockMoved, result.Error);
    }

    // A malformed request, not a situation: the domain throws and the API
    // turns it into a 400, same as for a product.
    [Fact]
    public async Task HandleAsync_WhenTheChangeIsZero_LetsTheDomainExceptionThrough()
    {
        (NightStocksInMemory stocks, _) = AStockWith(loaded: 5);

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => new AdjustNightStockHandler(stocks)
            .HandleAsync(new AdjustNightStockCommand(ANight, AProduct, 0), CancellationToken.None));

        Assert.Equal(NightStock.ErrorCodes.StockChangeZero, error.Code);
    }

    private static (NightStocksInMemory Stocks, NightStock Row) AStockWith(int loaded)
    {
        NightStock row = NightStock.Open(TheVenue, ANight, AProduct, loaded);
        NightStocksInMemory stocks = new(new NightForStock(ANight, DateTimeOffset.UtcNow, PreviousEndedAt: null));
        stocks.Existing.Add(row);

        return (stocks, row);
    }
}
