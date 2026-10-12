using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-37 against the real database: each bar (a KDS) keeps a stock of its
/// own, loaded for the night while it keeps selling, and the sheet reads back
/// what each bar started with, loaded, sold and left.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightStockTests(SqlServerFixture sql)
{
    // Friday 23:00 to Saturday 06:00, and Saturday the same hours.
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Saturday = Friday.AddDays(1);

    [Fact]
    public async Task HandleAsync_WhenTheNightIsOpen_LoadsTheBarAndRecordsTheAdjustment()
    {
        Seeded seeded = await Seed();

        Result<NightStockSheet> result = await Adjust(seeded, seeded.MainBar, seeded.Gin, 12, at: Saturday.AddHours(-5));

        Assert.True(result.IsSuccess);
        Assert.Equal(12, await UnitsIn(seeded, seeded.MainBar, seeded.Gin));
        Assert.Equal(0, await UnitsIn(seeded, seeded.VipBar, seeded.Gin));
        Assert.Equal(12, await TotalOf(seeded, seeded.Gin));

        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);
        NightStockAdjustment recorded = await read.NightStockAdjustments.SingleAsync();
        Assert.Equal((seeded.Saturday.Id, seeded.MainBar.Id, 12), (recorded.NightId, recorded.KdsId, recorded.Change));
    }

    [Fact]
    public async Task HandleAsync_WhenTheBarAlreadyHasSome_AddsToIt()
    {
        Seeded seeded = await Seed();

        await Adjust(seeded, seeded.MainBar, seeded.Gin, 12, at: Saturday);
        await Adjust(seeded, seeded.MainBar, seeded.Gin, -2, at: Saturday);

        Assert.Equal(10, await UnitsIn(seeded, seeded.MainBar, seeded.Gin));
        Assert.Equal(10, await TotalOf(seeded, seeded.Gin));
    }

    // The bar has 5, sells 3, and a correction of −5 arrives: it would leave −2.
    [Fact]
    public async Task HandleAsync_WhenTheBarHasLessThanItTakesAway_WritesNothing()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 5, at: Saturday);
        await Confirm(seeded, seeded.Gin, 3);

        Result<NightStockSheet> result = await Adjust(seeded, seeded.MainBar, seeded.Gin, -5, at: Saturday);

        Assert.Equal(ProductErrors.StockMoved, result.Error);
        Assert.Equal(2, await UnitsIn(seeded, seeded.MainBar, seeded.Gin));

        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);
        Assert.Equal(1, await fresh.NightStockAdjustments.CountAsync());
    }

    // Taking out of a bar that never had any is the same: nothing to take.
    [Fact]
    public async Task HandleAsync_WhenTheBarNeverHadAny_RefusesToTakeOut()
    {
        Seeded seeded = await Seed();

        Result<NightStockSheet> result = await Adjust(seeded, seeded.MainBar, seeded.Gin, -1, at: Saturday);

        Assert.Equal(ProductErrors.StockMoved, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductBelongsToAnotherVenue_FindsNothing()
    {
        Seeded seeded = await Seed();

        Result<NightStockSheet> result = await Adjust(seeded, seeded.MainBar, seeded.ForeignGin, 10, at: Saturday);

        Assert.Equal(ProductErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheBarBelongsToAnotherVenue_FindsNothing()
    {
        Seeded seeded = await Seed();

        Result<NightStockSheet> result = await Adjust(seeded, seeded.ForeignBar, seeded.Gin, 10, at: Saturday);

        Assert.Equal(NightErrors.BarNotFound, result.Error);
    }

    // Criterion 2, per bar: the order is taken from the bar it goes to.
    [Fact]
    public async Task Confirm_WhenOnlyOneBarHasIt_SendsTheOrderThereAndTakesItsUnits()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.VipBar, seeded.Gin, 5, at: Saturday);

        Result<ConfirmedOrder> result = await Confirm(seeded, seeded.Gin, 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(seeded.VipBar.Id, await BarOf(seeded, result.Value.Id));
        Assert.Equal(3, await UnitsIn(seeded, seeded.VipBar, seeded.Gin));
        Assert.Equal(3, await TotalOf(seeded, seeded.Gin));
    }

    // The first order queues at one bar; the second goes to the other.
    [Fact]
    public async Task Confirm_WhenBothBarsHaveIt_SendsTheOrderToTheOneWithLessWork()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 10, at: Saturday);
        await Adjust(seeded, seeded.VipBar, seeded.Gin, 10, at: Saturday);

        Result<ConfirmedOrder> first = await Confirm(seeded, seeded.Gin, 1);
        Result<ConfirmedOrder> second = await Confirm(seeded, seeded.Gin, 1);

        Assert.NotEqual(await BarOf(seeded, first.Value.Id), await BarOf(seeded, second.Value.Id));
    }

    // Two drinks, one in each bar: an order is not split, so it is refused.
    [Fact]
    public async Task Confirm_WhenNoBarHasItAll_RefusesAndTakesNothing()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 5, at: Saturday);
        await Adjust(seeded, seeded.VipBar, seeded.Fernet, 5, at: Saturday);

        Result<ConfirmedOrder> result = await Confirm(seeded, [new OrderLineRequest(seeded.Gin.Id, 1, null), new OrderLineRequest(seeded.Fernet.Id, 1, null)]);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Equal(5, await UnitsIn(seeded, seeded.MainBar, seeded.Gin));
        Assert.Equal(5, await UnitsIn(seeded, seeded.VipBar, seeded.Fernet));
    }

    // US-23 with bars: what a canceled order took goes back to its own bar.
    [Fact]
    public async Task Cancel_PutsTheUnitsBackInTheBarTheOrderWentTo()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.VipBar, seeded.Gin, 5, at: Saturday);
        Result<ConfirmedOrder> confirmed = await Confirm(seeded, seeded.Gin, 2, PaymentMethod.Cash);

        await using (DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id))
        {
            OrderRepository orders = new(context, new NoEvents(), NullLogger<OrderRepository>.Instance);
            Order order = (await orders.GetForUpdateAsync(OrderCode.Parse(confirmed.Value.Code), CancellationToken.None))!;
            order.Cancel();
            await orders.SaveCancellationAsync(order, CancellationToken.None);
        }

        Assert.Equal(5, await UnitsIn(seeded, seeded.VipBar, seeded.Gin));
        Assert.Equal(5, await TotalOf(seeded, seeded.Gin));
    }

    [Fact]
    public async Task HasUnfinishedBeforeAsync_WhileTheEarlierNightIsOn_IsTrueAndOnceItEnded_IsFalse()
    {
        Seeded seeded = await Seed();

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        NightRepository repository = new(context);

        Assert.True(await repository.HasUnfinishedBeforeAsync(Saturday, Friday.AddHours(2), CancellationToken.None));
        Assert.False(await repository.HasUnfinishedBeforeAsync(Saturday, seeded.Friday.EndsAt, CancellationToken.None));
        Assert.False(await repository.HasUnfinishedBeforeAsync(Friday, Friday.AddHours(2), CancellationToken.None));
    }

    [Fact]
    public async Task HasUnfinishedBeforeAsync_WhenOnlyAnotherVenueHasANightOn_IsFalse()
    {
        Seeded seeded = await Seed();

        await using DrinkItDbContext context = sql.CreateContext(seeded.Theirs.Id);

        Assert.False(await new NightRepository(context).HasUnfinishedBeforeAsync(Saturday, Friday.AddHours(2), CancellationToken.None));
    }

    // Criteria 1 and 3, bar by bar. The main bar loads 12 gins on Friday and
    // sells 7; Saturday it starts with the 5 left, loads 10 and sells 4. The
    // VIP bar only works Saturday, loads 6 and sells 1.
    [Fact]
    public async Task GetAsync_AfterTwoNights_SaysWhatEachBarStartedWithLoadedSoldAndLeft()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 12, at: Friday, night: seeded.Friday);
        await Confirm(seeded, seeded.Gin, 7, at: Friday.AddHours(1));
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 10, at: Saturday);
        await Adjust(seeded, seeded.VipBar, seeded.Gin, 6, at: Saturday);
        await Confirm(seeded, seeded.Gin, 4, at: Saturday.AddHours(1), bar: seeded.MainBar);
        await Confirm(seeded, seeded.Gin, 1, at: Saturday.AddHours(1), bar: seeded.VipBar);

        NightStockSheet friday = await SheetOf(seeded, seeded.Friday);
        NightStockSheet saturday = await SheetOf(seeded, seeded.Saturday);

        Assert.Equal(["main-bar"], friday.Bars.Select(bar => bar.Name));
        NightStockLine mainOnFriday = GinIn(friday, seeded.MainBar, seeded);
        Assert.Equal((0, 12, 12, 7, 5), (mainOnFriday.Carried, mainOnFriday.Added, mainOnFriday.Loaded, mainOnFriday.Sold, mainOnFriday.Left));

        Assert.Equal(["main-bar", "vip-bar"], saturday.Bars.Select(bar => bar.Name));
        NightStockLine mainOnSaturday = GinIn(saturday, seeded.MainBar, seeded);
        Assert.Equal((5, 10, 15, 4, 11), (mainOnSaturday.Carried, mainOnSaturday.Added, mainOnSaturday.Loaded, mainOnSaturday.Sold, mainOnSaturday.Left));
        NightStockLine vipOnSaturday = GinIn(saturday, seeded.VipBar, seeded);
        Assert.Equal((0, 6, 6, 1, 5), (vipOnSaturday.Carried, vipOnSaturday.Added, vipOnSaturday.Loaded, vipOnSaturday.Sold, vipOnSaturday.Left));
    }

    [Fact]
    public async Task GetAsync_ListsEveryProductOnTheMenuByNameAndLeavesOutDeletedAndForeignOnes()
    {
        Seeded seeded = await Seed();

        NightStockSheet saturday = await SheetOf(seeded, seeded.Saturday);

        NightStockBar main = saturday.Bars.Single(bar => bar.KdsId == seeded.MainBar.Id);
        Assert.Equal(["Fernet", "Gin Tonic"], main.Lines.Select(line => line.ProductName));
    }

    [Fact]
    public async Task GetAsync_ForAnotherVenuesNight_IsNull()
    {
        Seeded seeded = await Seed();

        await using DrinkItDbContext context = sql.CreateContext(seeded.Theirs.Id);

        Assert.Null(await new NightStockQueries(context).GetAsync(seeded.Friday.Id, CancellationToken.None));
    }

    private static NightStockLine GinIn(NightStockSheet sheet, StaffUser bar, Seeded seeded) =>
        sheet.Bars.Single(candidate => candidate.KdsId == bar.Id).Lines.Single(line => line.ProductId == seeded.Gin.Id);

    private async Task<NightStockSheet> SheetOf(Seeded seeded, Night night)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return (await new NightStockQueries(context).GetAsync(night.Id, CancellationToken.None))!;
    }

    private async Task<Result<NightStockSheet>> Adjust(Seeded seeded, StaffUser bar, Product product, int change, DateTimeOffset at, Night? night = null)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await new AdjustNightStockHandler(
                new NightRepository(context),
                new ProductRepository(context),
                new StaffUserRepository(context),
                new NightStockRepository(context),
                new NightStockQueries(context),
                new FixedClock(at))
            .HandleAsync(new AdjustNightStockCommand((night ?? seeded.Saturday).Id, bar.Id, product.Id, change), CancellationToken.None);
    }

    private Task<Result<ConfirmedOrder>> Confirm(
        Seeded seeded,
        Product product,
        int units,
        PaymentMethod method = PaymentMethod.Digital,
        DateTimeOffset? at = null,
        StaffUser? bar = null) =>
        Confirm(seeded, [new OrderLineRequest(product.Id, units, null)], method, at, bar);

    /// <summary>
    /// The real confirmation, against the real bars. <paramref name="bar"/>
    /// pins the order to one of them, by hiding the others from this one
    /// request: how the seeded history gets to each bar.
    /// </summary>
    private async Task<Result<ConfirmedOrder>> Confirm(
        Seeded seeded,
        OrderLineRequest[] lines,
        PaymentMethod method = PaymentMethod.Digital,
        DateTimeOffset? at = null,
        StaffUser? bar = null)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        FixedClock clock = new(at ?? Saturday.AddHours(1));
        FixedVenue venue = new(seeded.Venue.Id);
        IBarsForOrdering bars = new BarsForOrdering(context);

        return await new ConfirmOrderHandler(
                new OrderRepository(context, new NoEvents(), NullLogger<OrderRepository>.Instance),
                new ProductsForOrdering(context),
                bar is null ? bars : new OnlyBar(bars, bar.Id),
                new OrderCodeSequence(context, venue),
                [new DigitalPaymentStrategy(clock), new CashPaymentStrategy()],
                venue,
                new UnderwayNightLookup(context),
                clock)
            .HandleAsync(new ConfirmOrderCommand("María Quadro", method, Guid.NewGuid().ToString(), lines), CancellationToken.None);
    }

    private async Task<int> UnitsIn(Seeded seeded, StaffUser bar, Product product)
    {
        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);

        return await fresh.BarStocks
            .Where(row => row.KdsId == bar.Id && row.ProductId == product.Id)
            .Select(row => row.Units)
            .SingleOrDefaultAsync();
    }

    private async Task<int> TotalOf(Seeded seeded, Product product)
    {
        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);

        return (await fresh.Products.SingleAsync(row => row.Id == product.Id)).Stock;
    }

    private async Task<Guid?> BarOf(Seeded seeded, Guid orderId)
    {
        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);

        return await fresh.Orders.Where(order => order.Id == orderId).Select(order => order.KdsId).SingleAsync();
    }

    private sealed record Seeded(
        Venue Venue,
        Venue Theirs,
        StaffUser MainBar,
        StaffUser VipBar,
        StaffUser ForeignBar,
        Night Friday,
        Night Saturday,
        Product Gin,
        Product Fernet,
        Product ForeignGin);

    /// <summary>
    /// Friday is worked by the main bar alone; Saturday by both. Nothing is
    /// loaded yet: every bar starts empty, as a new product does since US-37.
    /// </summary>
    private async Task<Seeded> Seed()
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(mine.Id);
        Category theirCategory = SeedCategory.For(theirs.Id);
        StaffUser mainBar = StaffUser.Create(mine.Id, "main-bar", "hash", StaffRole.Kds);
        StaffUser vipBar = StaffUser.Create(mine.Id, "vip-bar", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(mine.Id, "till-1", "hash", StaffRole.Cashier);
        StaffUser foreignBar = StaffUser.Create(theirs.Id, "main-bar", "hash", StaffRole.Kds);

        Product retired = Product.Create(mine.Id, "Daiquiri", null, null, 8000m, 0, category.Id);
        retired.Deactivate();

        Seeded seeded = new(
            mine,
            theirs,
            mainBar,
            vipBar,
            foreignBar,
            Night.Create(mine.Id, "Friday", Friday, Friday.AddHours(7), [mainBar, till]),
            Night.Create(mine.Id, "Saturday", Saturday, Saturday.AddHours(7), [mainBar, vipBar, till]),
            Product.Create(mine.Id, "Gin Tonic", null, null, 9500m, 0, category.Id),
            Product.Create(mine.Id, "Fernet", null, null, 7000m, 0, category.Id),
            Product.Create(theirs.Id, "Gin Tonic", null, null, 9500m, 0, theirCategory.Id));

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.Categories.AddRange(category, theirCategory);
        seed.StaffUsers.AddRange(mainBar, vipBar, till, foreignBar);
        seed.Nights.AddRange(seeded.Friday, seeded.Saturday);
        seed.Products.AddRange(seeded.Gin, seeded.Fernet, retired, seeded.ForeignGin);
        await seed.SaveChangesAsync();

        return seeded;
    }

    private sealed class OnlyBar(IBarsForOrdering bars, Guid kdsId) : IBarsForOrdering
    {
        public async Task<IReadOnlyList<BarSupply>> GetAsync(Guid nightId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
            [.. (await bars.GetAsync(nightId, productIds, cancellationToken)).Where(bar => bar.KdsId == kdsId)];
    }

    private sealed class NoEvents : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id => id;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
