using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Menu;

/// <summary>
/// Adjusting a product's stock against the real database, where the two things
/// worth proving live: the bar keeps selling between the moment the product is
/// read and the moment the adjustment lands, and no sale may be lost to it; and
/// what is loaded into the bars of a night not yet over cannot be taken away
/// from here (US-37).
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class ProductStockAdjustmentTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Saturday = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    private static readonly DateTimeOffset Now = Saturday.AddHours(1);

    [Fact]
    public async Task HandleAsync_WhenTheProductIsThisVenues_WritesTheNewStock()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues(stock: 20);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Result<ProductSummary> result = await new AdjustProductStockHandler(new ProductRepository(asMine), new FixedClock(Now))
            .HandleAsync(new AdjustProductStockCommand(gin.Id, -15), CancellationToken.None);

        Assert.Equal(5, result.Value.Stock);
        Assert.Equal(5, await StockOf(mine, gin));
    }

    // The race: read 20, the bar sells 3, the delivery of 10 lands. The answer
    // is 27, not the 30 a total worked out in memory would write.
    [Fact]
    public async Task SaveStockAdjustmentAsync_WhenASaleLandedAfterTheRead_KeepsTheSale()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues(stock: 20);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        ProductRepository repository = new(asMine);
        Product read = (await repository.GetForUpdateAsync(gin.Id, CancellationToken.None))!;

        await SellMeanwhile(mine, gin, units: 3);

        read.AdjustStock(10);
        bool applied = await repository.SaveStockAdjustmentAsync(read, 10, Now, CancellationToken.None);

        Assert.True(applied);
        Assert.Equal(27, await StockOf(mine, gin));
        Assert.Equal(27, read.Stock);
    }

    // Read 5, the bar sells 3, a correction of −5 arrives: it would leave −3.
    [Fact]
    public async Task SaveStockAdjustmentAsync_WhenSalesLeftLessThanItTakesAway_WritesNothing()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues(stock: 5);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        ProductRepository repository = new(asMine);
        Product read = (await repository.GetForUpdateAsync(gin.Id, CancellationToken.None))!;

        await SellMeanwhile(mine, gin, units: 3);

        read.AdjustStock(-5);
        bool applied = await repository.SaveStockAdjustmentAsync(read, -5, Now, CancellationToken.None);

        Assert.False(applied);
        Assert.Equal(2, await StockOf(mine, gin));
        Assert.Equal(2, read.Stock);
    }

    // The common race, the one that takes minutes: the screen showed 5 and
    // worked out −5 from it, and by the time the save arrives the bar has sold
    // 3. The handler reads 2, refuses before writing, and the stock stays 2.
    [Fact]
    public async Task HandleAsync_WhenSalesSinceTheScreenOpenedLeftTooLittle_FailsWithStockMovedAndWritesNothing()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues(stock: 5);

        await SellMeanwhile(mine, gin, units: 3);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Result<ProductSummary> result = await new AdjustProductStockHandler(new ProductRepository(asMine), new FixedClock(Now))
            .HandleAsync(new AdjustProductStockCommand(gin.Id, -5), CancellationToken.None);

        Assert.Equal(ProductErrors.StockMoved, result.Error);
        Assert.Equal(2, await StockOf(mine, gin));
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductBelongsToAnotherVenue_FindsNothingAndWritesNothing()
    {
        (Venue mine, Venue theirs, _, Product foreign) = await SeedTwoVenues(stock: 20);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Result<ProductSummary> result = await new AdjustProductStockHandler(new ProductRepository(asMine), new FixedClock(Now))
            .HandleAsync(new AdjustProductStockCommand(foreign.Id, 10), CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.Equal(20, await StockOf(theirs, foreign));
    }

    // 20 in all, 15 of them loaded into Saturday's bar: only 5 are in the
    // storeroom, and taking 10 away would take units the bar is selling.
    [Fact]
    public async Task HandleAsync_WhenTheUnitsAreInABarOfANightNotYetOver_FailsWithStockMovedAndWritesNothing()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues(stock: 20);
        Night saturday = await LoadIntoABar(mine, gin, units: 15);

        Result<ProductSummary> result = await Adjust(mine, gin, -10, at: saturday.StartsAt.AddHours(1));

        Assert.Equal(ProductErrors.StockMoved, result.Error);
        Assert.Equal(20, await StockOf(mine, gin));
    }

    // A night that has not started yet already holds what was loaded into it.
    [Fact]
    public async Task HandleAsync_WhenTheUnitsAreInABarOfAnUpcomingNight_FailsWithStockMoved()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues(stock: 20);
        Night saturday = await LoadIntoABar(mine, gin, units: 15);

        Result<ProductSummary> result = await Adjust(mine, gin, -10, at: saturday.StartsAt.AddHours(-5));

        Assert.Equal(ProductErrors.StockMoved, result.Error);
    }

    // Once Saturday is over, what its bar did not sell is back in the storeroom.
    [Fact]
    public async Task HandleAsync_WhenTheNightThatHeldThemIsOver_TakesThemAway()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues(stock: 20);
        Night saturday = await LoadIntoABar(mine, gin, units: 15);

        Result<ProductSummary> result = await Adjust(mine, gin, -10, at: saturday.EndsAt);

        Assert.Equal(10, result.Value.Stock);
        Assert.Equal(10, await StockOf(mine, gin));
    }

    // Adding never depends on the bars.
    [Fact]
    public async Task HandleAsync_WhenItAdds_AddsWhateverTheBarsHold()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues(stock: 20);
        Night saturday = await LoadIntoABar(mine, gin, units: 20);

        Result<ProductSummary> result = await Adjust(mine, gin, 12, at: saturday.StartsAt.AddHours(1));

        Assert.Equal(32, result.Value.Stock);
    }

    private async Task<Result<ProductSummary>> Adjust(Venue venue, Product product, int change, DateTimeOffset at)
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        return await new AdjustProductStockHandler(new ProductRepository(context), new FixedClock(at))
            .HandleAsync(new AdjustProductStockCommand(product.Id, change), CancellationToken.None);
    }

    /// <summary>Saturday, worked by one bar holding <paramref name="units"/> of the product. The general stock is not touched: they came from the storeroom.</summary>
    private async Task<Night> LoadIntoABar(Venue venue, Product product, int units)
    {
        StaffUser bar = StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier);
        Night saturday = Night.Create(venue.Id, "Saturday", Saturday, Saturday.AddHours(7), [bar, till]);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.StaffUsers.AddRange(bar, till);
        seed.Nights.Add(saturday);
        seed.BarStocks.Add(new BarStock { VenueId = venue.Id, NightId = saturday.Id, KdsId = bar.Id, ProductId = product.Id, Units = units });
        await seed.SaveChangesAsync();

        return saturday;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    /// <summary>A sale as OrderRepository makes it: one conditional statement, from another context.</summary>
    private async Task SellMeanwhile(Venue venue, Product product, int units)
    {
        await using DrinkItDbContext bar = sql.CreateContext(venue.Id);

        await bar.Products
            .Where(row => row.Id == product.Id && row.Stock >= units)
            .ExecuteUpdateAsync(row => row.SetProperty(p => p.Stock, p => p.Stock - units));
    }

    private async Task<int> StockOf(Venue venue, Product product)
    {
        await using DrinkItDbContext fresh = sql.CreateContext(venue.Id);

        return (await fresh.Products.SingleAsync(row => row.Id == product.Id)).Stock;
    }

    private async Task<(Venue Mine, Venue Theirs, Product Gin, Product Foreign)> SeedTwoVenues(int stock)
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");
        Category mineCategory = SeedCategory.For(mine.Id);
        Category theirCategory = SeedCategory.For(theirs.Id);
        Product gin = Product.Create(mine.Id, "Gin Tonic", null, null, 4500m, stock, mineCategory.Id);
        Product foreign = Product.Create(theirs.Id, "Gin Tonic", null, null, 4500m, stock, theirCategory.Id);

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.Categories.AddRange(mineCategory, theirCategory);
        seed.Products.AddRange(gin, foreign);
        await seed.SaveChangesAsync();

        return (mine, theirs, gin, foreign);
    }
}
