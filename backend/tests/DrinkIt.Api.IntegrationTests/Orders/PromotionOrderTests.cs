using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Promotions;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Promotions;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Promotions;
using DrinkIt.Infrastructure.StampCards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// US-54 to US-56 against a real database: the price a promotion gives is
/// decided with the venue's clock, frozen in the order, and one venue's
/// promotions never reach another's.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class PromotionOrderTests(SqlServerFixture sql)
{
    // 23:30 of a Wednesday in Buenos Aires; 02:30 of Thursday UTC.
    private static readonly DateTimeOffset WednesdayNight = new(2026, 9, 17, 2, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Confirm_WhenAPromotionIsOn_ChargesTheDiscountAndKeepsItWhenThePromotionChangesLater()
    {
        Seeded seeded = await Seed();
        await AddPromotion(seeded, 30);

        Result<ConfirmedOrder> result = await Confirm(seeded, WednesdayNight, 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(6300m, result.Value.Total);

        await using (DrinkItDbContext write = sql.CreateContext(seeded.Venue.Id))
        {
            Promotion stored = await write.Promotions.SingleAsync();
            stored.Update("Happy hour", 50, [DayOfWeek.Wednesday], new TimeOnly(22, 0), new TimeOnly(1, 0), [seeded.Gin.Id]);
            await write.SaveChangesAsync();
        }

        OrderItem item = (await StoredOrder(seeded, result.Value.Id)).Items.Single();

        Assert.Equal(3150m, item.UnitPrice);
        Assert.Equal(4500m, item.RegularPrice);
        Assert.Equal("Happy hour", item.PromotionName);
    }

    [Fact]
    public async Task Confirm_WhenTwoPromotionsAreOn_AppliesOnlyTheBiggerOne()
    {
        Seeded seeded = await Seed();
        await AddPromotion(seeded, 20, "Twenty");
        await AddPromotion(seeded, 30, "Thirty");

        Result<ConfirmedOrder> result = await Confirm(seeded, WednesdayNight, 1);

        Assert.Equal(3150m, result.Value.Total);
    }

    [Fact]
    public async Task Confirm_WhenTheWindowIsClosed_ChargesTheListPrice()
    {
        Seeded seeded = await Seed();
        await AddPromotion(seeded, 30, start: new TimeOnly(12, 0), end: new TimeOnly(15, 0));

        Result<ConfirmedOrder> result = await Confirm(seeded, WednesdayNight, 2);

        Assert.Equal(9000m, result.Value.Total);
        Assert.Null((await StoredOrder(seeded, result.Value.Id)).Items.Single().RegularPrice);
    }

    // 00:30 of Thursday in Buenos Aires still belongs to the window that began
    // on Wednesday at 22:00.
    [Fact]
    public async Task Confirm_WhenTheWindowCrossedMidnightAtTheVenue_StillAppliesAfterMidnight()
    {
        DateTimeOffset afterMidnight = new(2026, 9, 17, 3, 30, 0, TimeSpan.Zero);
        Seeded seeded = await Seed(afterMidnight);
        await AddPromotion(seeded, 30);

        Result<ConfirmedOrder> result = await Confirm(seeded, afterMidnight, 1);

        Assert.Equal(3150m, result.Value.Total);
    }

    // The same 00:30 but of Wednesday: the window had not begun yet.
    [Fact]
    public async Task Confirm_WhenItIsTheEarlyMorningOfTheStartDay_ChargesTheListPrice()
    {
        DateTimeOffset earlyWednesday = new(2026, 9, 16, 3, 30, 0, TimeSpan.Zero);
        Seeded seeded = await Seed(earlyWednesday);
        await AddPromotion(seeded, 30);

        Result<ConfirmedOrder> result = await Confirm(seeded, earlyWednesday, 1);

        Assert.Equal(4500m, result.Value.Total);
    }

    // US-55, criterion 3: confirmed inside the window, collected after it ended.
    [Fact]
    public async Task Collect_WhenTheWindowEndedAfterAConfirmedCashOrder_ChargesThePromotionPrice()
    {
        Seeded seeded = await Seed();
        await AddPromotion(seeded, 30);
        Result<ConfirmedOrder> confirmed = await Confirm(seeded, WednesdayNight, 2, PaymentMethod.Cash);

        DateTimeOffset afterTheWindow = WednesdayNight.AddHours(3);
        Result<OrderStatus> collected = await Collect(seeded, confirmed.Value.Code, afterTheWindow);

        Assert.True(collected.IsSuccess);

        Order stored = await StoredOrder(seeded, confirmed.Value.Id);

        Assert.Equal(6300m, stored.Total);
    }

    // US-55, criterion 4: nothing is written and no stock moves.
    [Fact]
    public async Task Confirm_WhenTheTotalTheCustomerSawChanged_RefusesWritingNothingAndMovingNoStock()
    {
        Seeded seeded = await Seed();
        await AddPromotion(seeded, 30);

        Result<ConfirmedOrder> result = await Confirm(seeded, WednesdayNight, 2, expectedTotal: 9000m);

        Assert.Equal(OrderErrors.PriceChangedCode, result.Error!.Code);

        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);

        Assert.Empty(await read.Orders.ToListAsync());
        Assert.Equal(10, await read.BarStocks.Where(stock => stock.ProductId == seeded.Gin.Id).Select(stock => stock.Units).SingleAsync());
        Assert.Equal(10, await read.Products.Where(product => product.Id == seeded.Gin.Id).Select(product => product.Stock).SingleAsync());
    }

    // The invariant of the multi-tenant model, for promotions.
    [Fact]
    public async Task Promotions_WhenAnotherVenueAsks_AreNotVisibleNorReachable()
    {
        Seeded mine = await Seed();
        Seeded theirs = await Seed();
        await AddPromotion(mine, 30);

        await using DrinkItDbContext theirContext = sql.CreateContext(theirs.Venue.Id);
        Guid mineId = await sql.CreateContext(mine.Venue.Id).Promotions.Select(promotion => promotion.Id).SingleAsync();

        Assert.Empty(await new ActivePromotions(theirContext).ListAsync(CancellationToken.None));
        Assert.Empty(await new PromotionQueries(theirContext).ListAsync(CancellationToken.None));

        Result<PromotionSummary> update = await new UpdatePromotionHandler(new PromotionRepository(theirContext), new ProductsForOrdering(theirContext))
            .HandleAsync(new UpdatePromotionCommand(mineId, ADefinition(theirs.Gin.Id)), CancellationToken.None);

        Assert.Equal(PromotionErrors.NotFound, update.Error);

        Result<PromotionSummary> create = await new CreatePromotionHandler(new PromotionRepository(theirContext), new ProductsForOrdering(theirContext), new FixedVenue(theirs.Venue.Id))
            .HandleAsync(ADefinition(mine.Gin.Id), CancellationToken.None);

        Assert.Equal(PromotionErrors.ProductNotFound, create.Error);
    }

    [Fact]
    public async Task Menu_WhenAPromotionIsOn_ShowsThePriceWithItAndTheListPrice()
    {
        Seeded seeded = await Seed();
        await AddPromotion(seeded, 30);
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        IReadOnlyList<MenuItem> menu = await new ListMenuHandler(new ProductQueries(context), new ActivePromotions(context), new FixedClock(WednesdayNight))
            .HandleAsync(seeded.Night.Id, CancellationToken.None);

        MenuItem gin = menu.Single();

        Assert.Equal(3150m, gin.Price);
        Assert.Equal(4500m, gin.RegularPrice);
        Assert.Equal("Happy hour", gin.PromotionName);
    }

    private static PromotionDefinition ADefinition(Guid productId) =>
        new("Happy hour", 30, [DayOfWeek.Wednesday], new TimeOnly(22, 0), new TimeOnly(1, 0), [productId]);

    private async Task AddPromotion(Seeded seeded, int percent, string name = "Happy hour", TimeOnly? start = null, TimeOnly? end = null)
    {
        await using DrinkItDbContext write = sql.CreateContext(seeded.Venue.Id);
        write.Promotions.Add(Promotion.Create(seeded.Venue.Id, name, percent, [DayOfWeek.Wednesday], start ?? new TimeOnly(22, 0), end ?? new TimeOnly(1, 0), [seeded.Gin.Id]));
        await write.SaveChangesAsync();
    }

    private async Task<Order> StoredOrder(Seeded seeded, Guid id)
    {
        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);

        return await read.Orders.Include(order => order.Items).AsNoTracking().SingleAsync(order => order.Id == id);
    }

    private async Task<Result<ConfirmedOrder>> Confirm(
        Seeded seeded,
        DateTimeOffset at,
        int quantity,
        PaymentMethod method = PaymentMethod.Digital,
        decimal? expectedTotal = null)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        FixedVenue current = new(seeded.Venue.Id);
        FixedClock clock = new(at);

        return await new ConfirmOrderHandler(
            new OrderRepository(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance),
            new ProductsForOrdering(context),
            new BarsForOrdering(context),
            new OrderCodeSequence(context, current),
            [new DigitalPaymentStrategy(clock), new CashPaymentStrategy()],
            current,
            new UnderwayNightLookup(context),
            clock,
            new ActivePromotions(context),
            new StampCardRepository(context),
            new StampCardQueries(context))
            .HandleAsync(
                new ConfirmOrderCommand("María Quadro", method, Guid.NewGuid().ToString(), [new OrderLineRequest(seeded.Gin.Id, quantity, null)], ExpectedTotal: expectedTotal),
                CancellationToken.None);
    }

    private async Task<Result<OrderStatus>> Collect(Seeded seeded, string code, DateTimeOffset at)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await new CollectCashHandler(
            new OrderRepository(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance),
            new BarsForOrdering(context),
            new UnderwayNightLookup(context),
            new FixedClock(at),
            new TheStaffIs(seeded.Till.Id, "till-1"))
            .HandleAsync(code, CancellationToken.None);
    }

    private async Task<Seeded> Seed(DateTimeOffset? at = null)
    {
        DateTimeOffset around = at ?? WednesdayNight;
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 10, category.Id);
        StaffUser bar = StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier);
        Night night = Night.Create(venue.Id, "Tonight", around.AddHours(-3), around.AddHours(4), [bar, till]);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        seed.StaffUsers.AddRange(bar, till);
        seed.Nights.Add(night);
        seed.BarStocks.Add(new BarStock { VenueId = venue.Id, NightId = night.Id, KdsId = bar.Id, ProductId = gin.Id, Units = 10 });
        await seed.SaveChangesAsync();

        return new Seeded(venue, night, till, gin);
    }

    private sealed record Seeded(Venue Venue, Night Night, StaffUser Till, Product Gin);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id { get; } = id;
    }

    private sealed class TheStaffIs(Guid id, string username) : ICurrentStaffUser
    {
        public Guid? Id => id;

        public string? Username => username;
    }

    private sealed class NoDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
