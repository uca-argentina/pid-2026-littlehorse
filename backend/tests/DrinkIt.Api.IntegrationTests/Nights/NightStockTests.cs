using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-37 against the real database: loading a night's stock while the bar keeps
/// selling, and reading back what each night started with, loaded, sold and
/// left.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightStockTests(SqlServerFixture sql)
{
    // Friday 23:00 to Saturday 06:00, and Saturday the same hours.
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Saturday = Friday.AddDays(1);

    [Fact]
    public async Task HandleAsync_WhenTheNightIsOpen_MovesTheShelfAndRecordsTheAdjustment()
    {
        Seeded seeded = await Seed(ginStock: 20);

        Result<NightStockSheet> result = await Adjust(seeded.Venue, seeded.Saturday, seeded.Gin, 12, at: Saturday.AddHours(-5));

        Assert.True(result.IsSuccess);
        Assert.Equal(32, await ShelfOf(seeded.Venue, seeded.Gin));

        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);
        NightStockAdjustment recorded = await read.NightStockAdjustments.SingleAsync();
        Assert.Equal(seeded.Saturday.Id, recorded.NightId);
        Assert.Equal(seeded.Gin.Id, recorded.ProductId);
        Assert.Equal(12, recorded.Change);
        Assert.Equal(12, result.Value.Lines.Single(line => line.ProductId == seeded.Gin.Id).Added);
    }

    // The race: read 20, the bar sells 3, the delivery of 10 lands. The answer
    // is 27, not the 30 a total worked out in memory would write.
    [Fact]
    public async Task SaveStockAdjustmentAsync_WhenASaleLandedAfterTheRead_KeepsTheSale()
    {
        Seeded seeded = await Seed(ginStock: 20);

        await using DrinkItDbContext asMine = sql.CreateContext(seeded.Venue.Id);
        ProductRepository repository = new(asMine);
        Product read = (await repository.GetForUpdateAsync(seeded.Gin.Id, CancellationToken.None))!;
        Night night = (await new NightRepository(asMine).GetForUpdateAsync(seeded.Saturday.Id, CancellationToken.None))!;

        await SellMeanwhile(seeded.Venue, seeded.Gin, units: 3);

        NightStockAdjustment adjustment = night.AdjustStock(read, 10, Saturday);
        bool applied = await repository.SaveStockAdjustmentAsync(read, adjustment, CancellationToken.None);

        Assert.True(applied);
        Assert.Equal(27, await ShelfOf(seeded.Venue, seeded.Gin));
        Assert.Equal(27, read.Stock);
    }

    // Read 5, the bar sells 3, a correction of −5 arrives: it would leave −3.
    // Neither the shelf nor the record moves.
    [Fact]
    public async Task SaveStockAdjustmentAsync_WhenSalesLeftLessThanItTakesAway_WritesNothing()
    {
        Seeded seeded = await Seed(ginStock: 5);

        await using DrinkItDbContext asMine = sql.CreateContext(seeded.Venue.Id);
        ProductRepository repository = new(asMine);
        Product read = (await repository.GetForUpdateAsync(seeded.Gin.Id, CancellationToken.None))!;
        Night night = (await new NightRepository(asMine).GetForUpdateAsync(seeded.Saturday.Id, CancellationToken.None))!;

        await SellMeanwhile(seeded.Venue, seeded.Gin, units: 3);

        NightStockAdjustment adjustment = night.AdjustStock(read, -5, Saturday);
        bool applied = await repository.SaveStockAdjustmentAsync(read, adjustment, CancellationToken.None);

        Assert.False(applied);
        Assert.Equal(2, read.Stock);
        Assert.Equal(2, await ShelfOf(seeded.Venue, seeded.Gin));

        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);
        Assert.False(await fresh.NightStockAdjustments.AnyAsync());
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductBelongsToAnotherVenue_FindsNothingAndWritesNothing()
    {
        Seeded seeded = await Seed(ginStock: 20);

        Result<NightStockSheet> result = await Adjust(seeded.Venue, seeded.Saturday, seeded.ForeignGin, 10, at: Saturday);

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.Equal(20, await ShelfOf(seeded.Theirs, seeded.ForeignGin));
    }

    [Fact]
    public async Task HandleAsync_WhenTheNightBelongsToAnotherVenue_FindsNothing()
    {
        Seeded seeded = await Seed(ginStock: 20);

        Result<NightStockSheet> result = await Adjust(seeded.Theirs, seeded.Saturday, seeded.ForeignGin, 10, at: Saturday);

        Assert.Equal(NightErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task HasUnfinishedBeforeAsync_WhileTheEarlierNightIsOn_IsTrueAndOnceItEnded_IsFalse()
    {
        Seeded seeded = await Seed(ginStock: 20);

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        NightRepository repository = new(context);

        Assert.True(await repository.HasUnfinishedBeforeAsync(Saturday, Friday.AddHours(2), CancellationToken.None));
        Assert.False(await repository.HasUnfinishedBeforeAsync(Saturday, seeded.Friday.EndsAt, CancellationToken.None));
        Assert.False(await repository.HasUnfinishedBeforeAsync(Friday, Friday.AddHours(2), CancellationToken.None));
    }

    [Fact]
    public async Task HasUnfinishedBeforeAsync_WhenOnlyAnotherVenueHasANightOn_IsFalse()
    {
        Seeded seeded = await Seed(ginStock: 20);

        await using DrinkItDbContext context = sql.CreateContext(seeded.Theirs.Id);

        Assert.False(await new NightRepository(context).HasUnfinishedBeforeAsync(Saturday, Friday.AddHours(2), CancellationToken.None));
    }

    // Criteria 1 and 3 together. Gin: 20 on the shelf before Friday. Friday
    // loads 12 and sells 7 (a canceled order of 3 does not count). Saturday
    // loads 10 and sells 4. The shelf ends at 31.
    [Fact]
    public async Task GetAsync_AfterTwoNights_SaysWhatEachStartedWithLoadedSoldAndLeft()
    {
        Seeded seeded = await SeedTwoNightsOfSales();

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        NightStockQueries queries = new(context);
        NightStockSheet friday = (await queries.GetAsync(seeded.Friday.Id, CancellationToken.None))!;
        NightStockSheet saturday = (await queries.GetAsync(seeded.Saturday.Id, CancellationToken.None))!;

        NightStockLine ginOnFriday = friday.Lines.Single(line => line.ProductId == seeded.Gin.Id);
        Assert.Equal((20, 12, 32, 7, 25), (ginOnFriday.Carried, ginOnFriday.Added, ginOnFriday.Loaded, ginOnFriday.Sold, ginOnFriday.Left));

        NightStockLine ginOnSaturday = saturday.Lines.Single(line => line.ProductId == seeded.Gin.Id);
        Assert.Equal((25, 10, 35, 4, 31), (ginOnSaturday.Carried, ginOnSaturday.Added, ginOnSaturday.Loaded, ginOnSaturday.Sold, ginOnSaturday.Left));
    }

    // Criterion 1: nothing moved, so every night carries what is on the shelf.
    [Fact]
    public async Task GetAsync_ForAProductNobodyTouched_CarriesTheShelfAndListsItByName()
    {
        Seeded seeded = await SeedTwoNightsOfSales();

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        NightStockSheet friday = (await new NightStockQueries(context).GetAsync(seeded.Friday.Id, CancellationToken.None))!;

        Assert.Equal(["Fernet", "Gin Tonic"], friday.Lines.Select(line => line.ProductName));
        NightStockLine fernet = friday.Lines.Single(line => line.ProductId == seeded.Fernet.Id);
        Assert.Equal((8, 0, 0, 8), (fernet.Carried, fernet.Added, fernet.Sold, fernet.Left));
    }

    [Fact]
    public async Task GetAsync_LeavesOutDeletedProductsThatDidNotMoveAndOtherVenuesProducts()
    {
        Seeded seeded = await SeedTwoNightsOfSales();

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        NightStockSheet friday = (await new NightStockQueries(context).GetAsync(seeded.Friday.Id, CancellationToken.None))!;

        Assert.DoesNotContain(friday.Lines, line => line.ProductId == seeded.Retired.Id);
        Assert.DoesNotContain(friday.Lines, line => line.ProductId == seeded.ForeignGin.Id);
    }

    [Fact]
    public async Task GetAsync_ForAnotherVenuesNight_IsNull()
    {
        Seeded seeded = await Seed(ginStock: 20);

        await using DrinkItDbContext context = sql.CreateContext(seeded.Theirs.Id);

        Assert.Null(await new NightStockQueries(context).GetAsync(seeded.Friday.Id, CancellationToken.None));
    }

    private async Task<Result<NightStockSheet>> Adjust(Venue asVenue, Night night, Product product, int change, DateTimeOffset at)
    {
        await using DrinkItDbContext context = sql.CreateContext(asVenue.Id);

        return await new AdjustNightStockHandler(
                new NightRepository(context),
                new ProductRepository(context),
                new NightStockQueries(context),
                new FixedClock(at))
            .HandleAsync(new AdjustNightStockCommand(night.Id, product.Id, change), CancellationToken.None);
    }

    /// <summary>A sale as OrderRepository makes it: one conditional statement, from another context.</summary>
    private async Task SellMeanwhile(Venue venue, Product product, int units)
    {
        await using DrinkItDbContext bar = sql.CreateContext(venue.Id);

        await bar.Products
            .Where(row => row.Id == product.Id && row.Stock >= units)
            .ExecuteUpdateAsync(row => row.SetProperty(p => p.Stock, p => p.Stock - units));
    }

    private async Task<int> ShelfOf(Venue venue, Product product)
    {
        await using DrinkItDbContext fresh = sql.CreateContext(venue.Id);

        return (await fresh.Products.SingleAsync(row => row.Id == product.Id)).Stock;
    }

    private sealed record Seeded(
        Venue Venue,
        Venue Theirs,
        Night Friday,
        Night Saturday,
        Product Gin,
        Product Fernet,
        Product Retired,
        Product ForeignGin);

    private async Task<Seeded> Seed(int ginStock, Action<Seeded, DrinkItDbContext>? moreRows = null)
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(mine.Id);
        Category theirCategory = SeedCategory.For(theirs.Id);
        StaffUser[] crew = [StaffUser.Create(mine.Id, "main-bar", "hash", StaffRole.Kds), StaffUser.Create(mine.Id, "till-1", "hash", StaffRole.Cashier)];

        Product retired = Product.Create(mine.Id, "Daiquiri", null, null, 8000m, 4, category.Id);
        retired.Deactivate();

        Seeded seeded = new(
            mine,
            theirs,
            Night.Create(mine.Id, "Friday", Friday, Friday.AddHours(7), crew),
            Night.Create(mine.Id, "Saturday", Saturday, Saturday.AddHours(7), crew),
            Product.Create(mine.Id, "Gin Tonic", null, null, 9500m, ginStock, category.Id),
            Product.Create(mine.Id, "Fernet", null, null, 7000m, 8, category.Id),
            retired,
            Product.Create(theirs.Id, "Gin Tonic", null, null, 9500m, ginStock, theirCategory.Id));

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.Categories.AddRange(category, theirCategory);
        seed.StaffUsers.AddRange(crew);
        seed.Nights.AddRange(seeded.Friday, seeded.Saturday);
        moreRows?.Invoke(seeded, seed);
        seed.Products.AddRange(seeded.Gin, seeded.Fernet, seeded.Retired, seeded.ForeignGin);
        await seed.SaveChangesAsync();

        return seeded;
    }

    /// <summary>
    /// What two nights leave behind, written the way the app writes it: the
    /// shelf moved by every adjustment and every sale, the canceled order's
    /// units back on it.
    /// </summary>
    private Task<Seeded> SeedTwoNightsOfSales() => Seed(ginStock: 20, (seeded, context) =>
    {
        context.NightStockAdjustments.Add(seeded.Friday.AdjustStock(seeded.Gin, 12, Friday));
        AddOrder(context, seeded, seeded.Friday, "K-1001", 5);
        AddOrder(context, seeded, seeded.Friday, "K-1002", 2);
        AddOrder(context, seeded, seeded.Friday, "K-1003", 3, canceled: true);
        seeded.Gin.AdjustStock(-7);

        context.NightStockAdjustments.Add(seeded.Saturday.AdjustStock(seeded.Gin, 10, Saturday));
        AddOrder(context, seeded, seeded.Saturday, "K-1004", 4);
        seeded.Gin.AdjustStock(-4);
    });

    private static void AddOrder(DrinkItDbContext context, Seeded seeded, Night night, string code, int gins, bool canceled = false)
    {
        Order order = Order.Place(
            seeded.Venue.Id,
            night.Id,
            "María Quadro",
            OrderCode.Parse(code),
            [new NewOrderItem(seeded.Gin.Id, "Gin Tonic", 9500m, gins, null)]);
        order.AwaitPayment(PaymentMethod.Cash);
        if (canceled) order.Cancel();
        order.ClearDomainEvents();

        context.Orders.Add(order);
        context.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
