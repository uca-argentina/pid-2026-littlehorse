using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Tests.Nights;

/// <summary>
/// US-37, criterion 1: a night starts with what the one before it left, and
/// its stock does not exist until that one is over.
/// </summary>
public class OpenNightStockHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();
    private static readonly Guid ANight = Guid.CreateVersion7();

    private static readonly DateTimeOffset Saturday = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset SundayMorning = Saturday.AddHours(7);

    private static readonly StockedProduct GinTonic = new(Guid.CreateVersion7(), "Gin Tonic", 20);
    private static readonly StockedProduct Fernet = new(Guid.CreateVersion7(), "Fernet", 8);

    [Fact]
    public async Task HandleAsync_WhenTheVenueHasNoSuchNight_FailsWithNotFound()
    {
        NightStocksInMemory stocks = new(night: null, GinTonic);

        Result<IReadOnlyList<NightStockLine>> result = await HandlerOver(stocks, now: SundayMorning)
            .HandleAsync(ANight, CancellationToken.None);

        Assert.Equal(NightErrors.NotFound, result.Error);
        Assert.Empty(stocks.Added);
    }

    // The venue's first night has nothing before it: every product starts
    // with the number it had before nights owned the stock.
    [Fact]
    public async Task HandleAsync_WhenItIsTheFirstNight_OpensEachProductWithItsInitialStock()
    {
        NightStocksInMemory stocks = new(AFirstNight(), GinTonic, Fernet);

        Result<IReadOnlyList<NightStockLine>> result = await HandlerOver(stocks, now: Saturday.AddHours(-2))
            .HandleAsync(ANight, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [("Gin Tonic", 20, 0, 20), ("Fernet", 8, 0, 8)],
            result.Value.Select(line => (line.ProductName, line.Loaded, line.Sold, line.Remaining)));
        Assert.Equal(2, stocks.Added.Count);
        Assert.All(stocks.Added, row => Assert.Equal(ANight, row.NightId));
        Assert.All(stocks.Added, row => Assert.Equal(TheVenue, row.VenueId));
    }

    [Fact]
    public async Task HandleAsync_WhenThePreviousNightLeftUnits_OpensWithWhatItLeft()
    {
        NightStocksInMemory stocks = new(AnOverNightAfterAnother(), GinTonic, Fernet);
        stocks.CarriedOver[GinTonic.Id] = 3;

        Result<IReadOnlyList<NightStockLine>> result = await HandlerOver(stocks, now: SundayMorning)
            .HandleAsync(ANight, CancellationToken.None);

        NightStockLine gin = result.Value.Single(line => line.ProductId == GinTonic.Id);
        NightStockLine fernet = result.Value.Single(line => line.ProductId == Fernet.Id);

        // Gin Tonic had a row last night, so it carries what was left; Fernet
        // had none, so it starts from the number it was created with.
        Assert.Equal(3, gin.Loaded);
        Assert.Equal(8, fernet.Loaded);
    }

    [Fact]
    public async Task HandleAsync_WhenThePreviousNightLeftNothing_OpensSoldOut()
    {
        NightStocksInMemory stocks = new(AnOverNightAfterAnother(), GinTonic);
        stocks.CarriedOver[GinTonic.Id] = 0;

        Result<IReadOnlyList<NightStockLine>> result = await HandlerOver(stocks, now: SundayMorning)
            .HandleAsync(ANight, CancellationToken.None);

        Assert.Equal(0, result.Value.Single().Remaining);
        Assert.Equal(0, result.Value.Single().Loaded);
    }

    [Fact]
    public async Task HandleAsync_WhenThePreviousNightIsStillOn_FailsAndOpensNothing()
    {
        NightStocksInMemory stocks = new(AnOverNightAfterAnother(), GinTonic);

        Result<IReadOnlyList<NightStockLine>> result = await HandlerOver(stocks, now: SundayMorning.AddMinutes(-1))
            .HandleAsync(ANight, CancellationToken.None);

        Assert.Equal(NightErrors.PreviousNightNotOver, result.Error);
        Assert.Empty(stocks.Added);
    }

    // The end is exclusive: at 06:00 on the dot the night before is over.
    [Fact]
    public async Task HandleAsync_WhenThePreviousNightEndsRightNow_Opens()
    {
        NightStocksInMemory stocks = new(AnOverNightAfterAnother(), GinTonic);

        Result<IReadOnlyList<NightStockLine>> result = await HandlerOver(stocks, now: SundayMorning)
            .HandleAsync(ANight, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WhenTheStockWasAlreadyOpened_ReadsItWithoutOpeningAgain()
    {
        NightStocksInMemory stocks = new(AFirstNight(), GinTonic);
        stocks.Existing.Add(NightStock.Open(TheVenue, ANight, GinTonic.Id, 15));

        Result<IReadOnlyList<NightStockLine>> result = await HandlerOver(stocks, now: Saturday)
            .HandleAsync(ANight, CancellationToken.None);

        Assert.Equal(15, result.Value.Single().Loaded);
        Assert.Empty(stocks.Added);
    }

    // A product made after the night's stock was opened still has to be sold.
    [Fact]
    public async Task HandleAsync_WhenAProductHasNoRowYet_OpensOnlyThatOne()
    {
        NightStocksInMemory stocks = new(AFirstNight(), GinTonic, Fernet);
        stocks.Existing.Add(NightStock.Open(TheVenue, ANight, GinTonic.Id, 15));

        Result<IReadOnlyList<NightStockLine>> result = await HandlerOver(stocks, now: Saturday)
            .HandleAsync(ANight, CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
        Assert.Equal(Fernet.Id, stocks.Added.Single().ProductId);
    }

    private static NightForStock AFirstNight() => new(ANight, Saturday, PreviousEndedAt: null);

    private static NightForStock AnOverNightAfterAnother() => new(ANight, SundayMorning, PreviousEndedAt: SundayMorning);

    private static OpenNightStockHandler HandlerOver(NightStocksInMemory stocks, DateTimeOffset now) =>
        new(stocks, new TheCurrentVenue(), new FixedClock(now));

    private sealed class TheCurrentVenue : ICurrentVenue
    {
        public Guid Id => TheVenue;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
