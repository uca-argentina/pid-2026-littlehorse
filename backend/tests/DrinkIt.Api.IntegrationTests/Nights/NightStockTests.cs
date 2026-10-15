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
using DrinkIt.Infrastructure.Tables;
using DrinkIt.Infrastructure.VipAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-37 against the real database. The product keeps the general stock; each
/// night opens with its bars (its KDS) empty, and the administrator moves units
/// from the storeroom into them. The storeroom is the general stock minus what
/// the bars of nights not yet over hold, so what a bar did not sell is back in
/// it as soon as its night ends.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightStockTests(SqlServerFixture sql)
{
    // Friday 23:00 to Saturday 06:00, and Saturday the same hours.
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Saturday = Friday.AddDays(1);

    [Fact]
    public async Task HandleAsync_WhenTheStoreroomHasEnough_LoadsTheBarAndLeavesTheGeneralStockAlone()
    {
        Seeded seeded = await Seed();

        Result<NightStockSheet> result = await Adjust(seeded, seeded.MainBar, seeded.Gin, 12, at: Saturday.AddHours(-5));

        Assert.True(result.IsSuccess);
        Assert.Equal(12, await UnitsIn(seeded, seeded.MainBar, seeded.Gin));
        Assert.Equal(0, await UnitsIn(seeded, seeded.VipBar, seeded.Gin));
        Assert.Equal(30, await TotalOf(seeded, seeded.Gin));

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
        Assert.Equal(30, await TotalOf(seeded, seeded.Gin));
    }

    // 30 gins: 25 go to the main bar, and only 5 are left for the VIP one.
    [Fact]
    public async Task HandleAsync_WhenTheStoreroomHasLessThanItLoads_FailsWithStoreroomShortAndWritesNothing()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 25, at: Saturday);

        Result<NightStockSheet> result = await Adjust(seeded, seeded.VipBar, seeded.Gin, 10, at: Saturday);

        Assert.Equal(NightErrors.StoreroomShort, result.Error);
        Assert.Equal(0, await UnitsIn(seeded, seeded.VipBar, seeded.Gin));

        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);
        Assert.Equal(1, await fresh.NightStockAdjustments.CountAsync());
    }

    // Saturday loaded ahead of time: those units are not Friday's to take.
    [Fact]
    public async Task HandleAsync_WhenAnUpcomingNightAlreadyHoldsSome_LeavesThatMuchLessInTheStoreroom()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 25, at: Friday);

        Result<NightStockSheet> result = await Adjust(seeded, seeded.MainBar, seeded.Gin, 10, at: Friday, night: seeded.Friday);

        Assert.Equal(NightErrors.StoreroomShort, result.Error);
    }

    // Friday loads 12 and sells 7. Once it is over, the 5 left are back in
    // the storeroom, and Saturday's bar opens empty rather than with them.
    [Fact]
    public async Task HandleAsync_WhenTheEarlierNightIsOver_ItsLeftoversAreBackInTheStoreroom()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 12, at: Friday, night: seeded.Friday);
        await Confirm(seeded, seeded.Gin, 7, at: Friday.AddHours(1));

        Result<NightStockSheet> result = await Adjust(seeded, seeded.MainBar, seeded.Gin, 23, at: seeded.Friday.EndsAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(23, await UnitsIn(seeded, seeded.MainBar, seeded.Gin));
        Assert.Equal(23, await TotalOf(seeded, seeded.Gin));
    }

    // Nothing waits for the night before to end any more.
    [Fact]
    public async Task HandleAsync_WhileAnEarlierNightIsStillOn_LoadsTheNextOne()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 12, at: Friday, night: seeded.Friday);

        Result<NightStockSheet> result = await Adjust(seeded, seeded.MainBar, seeded.Gin, 10, at: Friday.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(10, await UnitsIn(seeded, seeded.MainBar, seeded.Gin));
        Assert.Equal(12, await UnitsIn(seeded, seeded.MainBar, seeded.Gin, seeded.Friday));
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

    // Taken back out of the bar, the units are in the storeroom again.
    [Fact]
    public async Task HandleAsync_WhenUnitsAreTakenOut_TheyCanBeLoadedIntoAnotherBar()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 30, at: Saturday);
        await Adjust(seeded, seeded.MainBar, seeded.Gin, -10, at: Saturday);

        Result<NightStockSheet> result = await Adjust(seeded, seeded.VipBar, seeded.Gin, 10, at: Saturday);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, await TotalOf(seeded, seeded.Gin));
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

    // Criterion 2, per bar: the order is taken from the bar it goes to, and
    // from the general stock with it.
    [Fact]
    public async Task Confirm_WhenOnlyOneBarHasIt_SendsTheOrderThereAndTakesItsUnits()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.VipBar, seeded.Gin, 5, at: Saturday);

        Result<ConfirmedOrder> result = await Confirm(seeded, seeded.Gin, 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(seeded.VipBar.Id, await BarOf(seeded, result.Value.Id));
        Assert.Equal(3, await UnitsIn(seeded, seeded.VipBar, seeded.Gin));
        Assert.Equal(28, await TotalOf(seeded, seeded.Gin));
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

    // Friday's bar is Friday's: on Saturday it serves nothing, even with
    // units left, until Saturday's own bar is loaded.
    [Fact]
    public async Task Confirm_WhenOnlyAnEarlierNightsBarHasIt_Refuses()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 5, at: Friday, night: seeded.Friday);

        Result<ConfirmedOrder> result = await Confirm(seeded, seeded.Gin, 1, at: Saturday.AddHours(1));

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Equal(30, await TotalOf(seeded, seeded.Gin));
    }

    // US-23 with bars: what a canceled order took goes back to its own bar,
    // and to the general stock with it.
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
        Assert.Equal(30, await TotalOf(seeded, seeded.Gin));
    }

    // Created with both bars: 30 gins and 10 fernets in the storeroom, split
    // evenly. The general stock does not move, and each load is on record.
    [Fact]
    public async Task CreateNight_SplitsTheStoreroomEvenlyAmongItsKds()
    {
        Seeded seeded = await Seed();

        Result<NightSummary> created = await CreateSunday(seeded, at: Saturday.AddDays(1).AddHours(-5));

        Night sunday = await NightNamed(seeded, created.Value.Id);
        Assert.Equal(15, await UnitsIn(seeded, seeded.MainBar, seeded.Gin, sunday));
        Assert.Equal(15, await UnitsIn(seeded, seeded.VipBar, seeded.Gin, sunday));
        Assert.Equal(5, await UnitsIn(seeded, seeded.MainBar, seeded.Fernet, sunday));
        Assert.Equal(5, await UnitsIn(seeded, seeded.VipBar, seeded.Fernet, sunday));
        Assert.Equal(30, await TotalOf(seeded, seeded.Gin));

        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);
        Assert.Equal(4, await fresh.NightStockAdjustments.CountAsync(adjustment => adjustment.NightId == sunday.Id));
    }

    // Saturday already holds 25 gins, so only 5 are left to split: 2 each,
    // and the one left over stays in the storeroom.
    [Fact]
    public async Task CreateNight_OnlySplitsWhatOtherNightsLeftInTheStoreroom()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 25, at: Friday);

        Result<NightSummary> created = await CreateSunday(seeded, at: Friday);

        Night sunday = await NightNamed(seeded, created.Value.Id);
        Assert.Equal(2, await UnitsIn(seeded, seeded.MainBar, seeded.Gin, sunday));
        Assert.Equal(2, await UnitsIn(seeded, seeded.VipBar, seeded.Gin, sunday));
        NightStockLine left = GinIn(await SheetOf(seeded, sunday, at: Friday), seeded.MainBar, seeded);
        Assert.Equal(1, left.InStoreroom);
    }

    // Set up with hours already past: created, and with nothing loaded.
    [Fact]
    public async Task CreateNight_WhenItsHoursAlreadyEnded_CreatesItWithNothingLoaded()
    {
        Seeded seeded = await Seed();

        Result<NightSummary> created = await CreateSunday(seeded, at: Saturday.AddDays(2));

        Assert.True(created.IsSuccess);
        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);
        Assert.False(await fresh.BarStocks.AnyAsync(stock => stock.NightId == created.Value.Id));
    }

    // A KDS with stock loaded cannot leave the night: nothing could sell it,
    // show it or give it back. Once it is given back, it can.
    [Fact]
    public async Task UpdateNight_WhenItTakesOutAKdsThatHoldsStock_RefusesUntilItIsGivenBack()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.VipBar, seeded.Gin, 6, at: Saturday);

        Result<NightSummary> refused = await TakeTheVipBarOut(seeded);
        await Adjust(seeded, seeded.VipBar, seeded.Gin, -6, at: Saturday);
        Result<NightSummary> accepted = await TakeTheVipBarOut(seeded);

        Assert.Equal(NightErrors.BarHoldsStock, refused.Error);
        Assert.True(accepted.IsSuccess);
    }

    // A queue left over from Friday is not work on Saturday: both bars are
    // equally free tonight, so the order goes to the first by name.
    [Fact]
    public async Task Confirm_WhenABarHasOrdersLeftFromAnEarlierNight_CountsOnlyTonightsWork()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 5, at: Friday, night: seeded.Friday);
        await Confirm(seeded, seeded.Gin, 1, at: Friday.AddHours(1));
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 5, at: Saturday);
        await Adjust(seeded, seeded.VipBar, seeded.Gin, 5, at: Saturday);

        Result<ConfirmedOrder> tonight = await Confirm(seeded, seeded.Gin, 1);

        Assert.Equal(seeded.MainBar.Id, await BarOf(seeded, tonight.Value.Id));
    }

    // Loading a bar locks the product and then writes the bar; a sale has to
    // take them in that same order, or the two can wait on each other. Here
    // the load holds the product, a sale starts, and the load then writes the
    // bar: with the sale waiting on the product, the bar is free.
    [Fact]
    public async Task Confirm_WhileTheProductIsLockedForALoad_WaitsBeforeTouchingTheBar()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 5, at: Saturday);

        await using DrinkItDbContext loader = sql.CreateContext(seeded.Venue.Id);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction load = await loader.Database.BeginTransactionAsync();
        await loader.Database.ExecuteSqlAsync($"SELECT [Id] FROM [Products] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {seeded.Gin.Id}");

        Task<Result<ConfirmedOrder>> sale = Confirm(seeded, seeded.Gin, 1, bar: seeded.MainBar);
        await Task.Delay(TimeSpan.FromSeconds(1));

        await loader.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 2000");
        int written = await loader.BarStocks
            .Where(stock => stock.NightId == seeded.Saturday.Id && stock.KdsId == seeded.MainBar.Id && stock.ProductId == seeded.Gin.Id)
            .ExecuteUpdateAsync(row => row.SetProperty(stock => stock.Units, stock => stock.Units + 1));
        await load.CommitAsync();

        Assert.Equal(1, written);
        Assert.True((await sale).IsSuccess);
        Assert.Equal(5, await UnitsIn(seeded, seeded.MainBar, seeded.Gin));
    }

    // Criterion 3, bar by bar. The main bar loads 12 gins on Friday and sells
    // 7; Saturday it opens empty, loads 10 and sells 4. The VIP bar only works
    // Saturday, loads 6 and sells 1. Looked at during Saturday: of the 18 gins
    // in all, Saturday's bars hold 11, so 7 are in the storeroom.
    [Fact]
    public async Task GetAsync_AfterTwoNights_SaysWhatEachBarLoadedSoldAndLeft()
    {
        Seeded seeded = await Seed();
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 12, at: Friday, night: seeded.Friday);
        await Confirm(seeded, seeded.Gin, 7, at: Friday.AddHours(1));
        await Adjust(seeded, seeded.MainBar, seeded.Gin, 10, at: Saturday);
        await Adjust(seeded, seeded.VipBar, seeded.Gin, 6, at: Saturday);
        await Confirm(seeded, seeded.Gin, 4, at: Saturday.AddHours(1), bar: seeded.MainBar);
        await Confirm(seeded, seeded.Gin, 1, at: Saturday.AddHours(1), bar: seeded.VipBar);

        NightStockSheet friday = await SheetOf(seeded, seeded.Friday, at: Saturday.AddHours(2));
        NightStockSheet saturday = await SheetOf(seeded, seeded.Saturday, at: Saturday.AddHours(2));

        Assert.Equal(["main-bar"], friday.Bars.Select(bar => bar.Name));
        NightStockLine mainOnFriday = GinIn(friday, seeded.MainBar, seeded);
        Assert.Equal((12, 7, 5), (mainOnFriday.Loaded, mainOnFriday.Sold, mainOnFriday.Left));

        Assert.Equal(["main-bar", "vip-bar"], saturday.Bars.Select(bar => bar.Name));
        NightStockLine mainOnSaturday = GinIn(saturday, seeded.MainBar, seeded);
        Assert.Equal((10, 4, 6, 7), (mainOnSaturday.Loaded, mainOnSaturday.Sold, mainOnSaturday.Left, mainOnSaturday.InStoreroom));
        NightStockLine vipOnSaturday = GinIn(saturday, seeded.VipBar, seeded);
        Assert.Equal((6, 1, 5, 7), (vipOnSaturday.Loaded, vipOnSaturday.Sold, vipOnSaturday.Left, vipOnSaturday.InStoreroom));
    }

    [Fact]
    public async Task GetAsync_ListsEveryProductOnTheMenuByNameAndLeavesOutDeletedAndForeignOnes()
    {
        Seeded seeded = await Seed();

        NightStockSheet saturday = await SheetOf(seeded, seeded.Saturday, at: Saturday);

        NightStockBar main = saturday.Bars.Single(bar => bar.KdsId == seeded.MainBar.Id);
        Assert.Equal(["Fernet", "Gin Tonic"], main.Lines.Select(line => line.ProductName));
    }

    [Fact]
    public async Task GetAsync_ForAnotherVenuesNight_IsNull()
    {
        Seeded seeded = await Seed();

        await using DrinkItDbContext context = sql.CreateContext(seeded.Theirs.Id);

        Assert.Null(await new NightStockQueries(context, new FixedClock(Saturday)).GetAsync(seeded.Friday.Id, CancellationToken.None));
    }

    /// <summary>Sunday, worked by both bars, created as the administration does it.</summary>
    private async Task<Result<NightSummary>> CreateSunday(Seeded seeded, DateTimeOffset at)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        DateTimeOffset sunday = Saturday.AddDays(1);

        return await new CreateNightHandler(
                new NightRepository(context),
                new StaffUserRepository(context),
                new TableRepository(context),
                new VipAccountRepository(context),
                new FixedVenue(seeded.Venue.Id),
                new FixedClock(at))
            .HandleAsync(new CreateNightCommand("Sunday", sunday, sunday.AddHours(7), [seeded.MainBar.Id, seeded.VipBar.Id, seeded.Till.Id]), CancellationToken.None);
    }

    private async Task<Result<NightSummary>> TakeTheVipBarOut(Seeded seeded)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await new UpdateNightHandler(new NightRepository(context), new StaffUserRepository(context), new TableRepository(context), new VipAccountRepository(context), new FixedClock(Saturday.AddHours(-1)))
            .HandleAsync(
                new UpdateNightCommand(seeded.Saturday.Id, "Saturday", Saturday, Saturday.AddHours(7), [seeded.MainBar.Id, seeded.Till.Id]),
                CancellationToken.None);
    }

    private async Task<Night> NightNamed(Seeded seeded, Guid id)
    {
        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);

        return await fresh.Nights.SingleAsync(night => night.Id == id);
    }

    private static NightStockLine GinIn(NightStockSheet sheet, StaffUser bar, Seeded seeded) =>
        sheet.Bars.Single(candidate => candidate.KdsId == bar.Id).Lines.Single(line => line.ProductId == seeded.Gin.Id);

    private async Task<NightStockSheet> SheetOf(Seeded seeded, Night night, DateTimeOffset at)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return (await new NightStockQueries(context, new FixedClock(at)).GetAsync(night.Id, CancellationToken.None))!;
    }

    private async Task<Result<NightStockSheet>> Adjust(Seeded seeded, StaffUser bar, Product product, int change, DateTimeOffset at, Night? night = null)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await new AdjustNightStockHandler(
                new NightRepository(context),
                new ProductRepository(context),
                new StaffUserRepository(context),
                new NightStockRepository(context),
                new NightStockQueries(context, new FixedClock(at)),
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

    /// <summary>What the bar holds of it for a night, Saturday unless said otherwise.</summary>
    private async Task<int> UnitsIn(Seeded seeded, StaffUser bar, Product product, Night? night = null)
    {
        await using DrinkItDbContext fresh = sql.CreateContext(seeded.Venue.Id);
        Guid nightId = (night ?? seeded.Saturday).Id;

        return await fresh.BarStocks
            .Where(row => row.NightId == nightId && row.KdsId == bar.Id && row.ProductId == product.Id)
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
        StaffUser Till,
        StaffUser ForeignBar,
        Night Friday,
        Night Saturday,
        Product Gin,
        Product Fernet,
        Product ForeignGin);

    /// <summary>
    /// Friday is worked by the main bar alone; Saturday by both. 30 gins and 10
    /// fernets in all, every one of them in the storeroom: no bar holds any yet.
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
            till,
            foreignBar,
            Night.Create(mine.Id, "Friday", Friday, Friday.AddHours(7), [mainBar, till]),
            Night.Create(mine.Id, "Saturday", Saturday, Saturday.AddHours(7), [mainBar, vipBar, till]),
            Product.Create(mine.Id, "Gin Tonic", null, null, 9500m, 30, category.Id),
            Product.Create(mine.Id, "Fernet", null, null, 7000m, 10, category.Id),
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
