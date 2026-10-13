using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Cashier;
using DrinkIt.Infrastructure.Kds;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Cashier;

/// <summary>
/// US-24 to US-26 against a real database: an order waiting for cash stays off
/// the bar's board, the till finds it by code, and collecting it is written and
/// puts it on the board of its own venue only.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class CashierTests(SqlServerFixture sql)
{
    private readonly Dictionary<Guid, Seeded> _seeded = [];

    private static readonly DateTimeOffset Tonight = new(2026, 9, 28, 1, 12, 0, TimeSpan.Zero);

    // US-24, criterion 3.
    [Fact]
    public async Task GetQueueAsync_WhenAnOrderAwaitsCash_LeavesItOffTheBoard()
    {
        Venue venue = await ASeededVenue();
        await AnOrderWaitingForCash(venue);

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        IReadOnlyList<KdsQueueOrder> queue = await new KdsQueueQueries(context, new TheKdsIs(_seeded[venue.Id].BarId))
            .GetQueueAsync(CancellationToken.None);

        Assert.Empty(queue);
    }

    // US-26, criterion 2: what the till still has to collect.
    [Fact]
    public async Task GetAwaitingPaymentAsync_Always_ListsOnlyWhatWaitsForCash()
    {
        Venue venue = await ASeededVenue();
        Order waiting = await AnOrderWaitingForCash(venue);
        Order collected = await AnOrderWaitingForCash(venue);
        await CollectAsync(venue, collected.Code.Value);

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        IReadOnlyList<CashierOrder> pending = await new CashierQueries(context).GetAwaitingPaymentAsync(CancellationToken.None);

        CashierOrder only = Assert.Single(pending);
        Assert.Equal(waiting.Code.Value, only.Code);
        Assert.Equal(9000m, only.Total);
    }

    // US-26, criterion 1: the drinks and what to charge.
    [Fact]
    public async Task FindAsync_WhenTheCodeIsThisVenues_ShowsTheDrinksAndTheTotal()
    {
        Venue venue = await ASeededVenue();
        Order order = await AnOrderWaitingForCash(venue);

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        CashierOrder? found = await new CashierQueries(context).FindAsync(order.Code.Value, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(OrderStatus.AwaitingPayment, found.Status);
        Assert.Equal(9000m, found.Total);
        CashierOrderItem item = Assert.Single(found.Items);
        Assert.Equal(("Gin Tonic", 2, "sin hielo", 4500m), (item.ProductName, item.Quantity, item.Note, item.UnitPrice));
    }

    // The invariant CLAUDE.md asks for on everything that touches Order.
    [Fact]
    public async Task FindAsync_WhenTheCodeIsAnotherVenues_FindsNothing()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Order order = await AnOrderWaitingForCash(theirs);

        await using DrinkItDbContext context = sql.CreateContext(mine.Id);
        CashierOrder? found = await new CashierQueries(context).FindAsync(order.Code.Value, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetAwaitingPaymentAsync_WhenAnotherVenueHasOrdersWaiting_ListsNoneOfThem()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        await AnOrderWaitingForCash(theirs);

        await using DrinkItDbContext context = sql.CreateContext(mine.Id);

        Assert.Empty(await new CashierQueries(context).GetAwaitingPaymentAsync(CancellationToken.None));
    }

    // US-26, criterion 2, and US-25, criterion 3: paid, queued, on the board.
    [Fact]
    public async Task Collect_WhenTheOrderAwaitsCash_StoresItPaidAndQueuedAndNotifiesTheBoard()
    {
        Venue venue = await ASeededVenue();
        Order order = await AnOrderWaitingForCash(venue);
        SpyDispatcher spy = new();

        await CollectAsync(venue, order.Code.Value, spy);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Order stored = await check.Orders.SingleAsync(row => row.Id == order.Id);
        Assert.Equal(OrderStatus.Queued, stored.Status);
        Assert.Equal(Tonight, stored.PaidAt);
        Assert.Equal("laura.caja", stored.CollectedBy);
        Assert.Equal(venue.Id, Assert.IsType<OrderQueued>(Assert.Single(spy.Received.OfType<OrderQueued>())).VenueId);
        Assert.Equal(venue.Id, Assert.Single(spy.Received.OfType<OrderCollected>()).VenueId);
    }

    // US-36: cash takes its drinks when it is collected, from the bar it goes
    // to then — and the general stock with them, as every sale does (US-37).
    [Fact]
    public async Task Collect_WhenTheOrderAwaitsCash_SendsItToTheBarAndTakesTheDrinksFromIt()
    {
        Venue venue = await ASeededVenue(barUnits: 20);
        Order order = await AnOrderWaitingForCash(venue);
        Seeded seeded = _seeded[venue.Id];

        await CollectAsync(venue, order.Code.Value);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Assert.Equal(seeded.BarId, (await check.Orders.SingleAsync(row => row.Id == order.Id)).KdsId);
        Assert.Equal(18, (await check.BarStocks.SingleAsync()).Units);
        Assert.Equal(18, (await check.Products.SingleAsync()).Stock);
    }

    // US-36: what was there when the customer confirmed went in the meantime.
    // The till hears it before the money changes hands, and nothing moves.
    [Fact]
    public async Task Collect_WhenNoBarHasTheDrinksAnyMore_RefusesAndTakesNothing()
    {
        Venue venue = await ASeededVenue(barUnits: 1);
        Order order = await AnOrderWaitingForCash(venue);

        Result<OrderStatus> result = await CollectAsync(venue, order.Code.Value);

        Assert.Equal(CashierErrors.SoldOut.Code, result.Error!.Code);
        Assert.Equal("Gin Tonic", result.Error.Subject);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Order stored = await check.Orders.SingleAsync(row => row.Id == order.Id);
        Assert.Equal(OrderStatus.AwaitingPayment, stored.Status);
        Assert.Null(stored.KdsId);
        Assert.Equal(1, (await check.BarStocks.SingleAsync()).Units);
        Assert.Equal(20, (await check.Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Collect_WhenTheOrderBelongsToAnotherVenue_FindsNothingAndChangesNothing()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Order order = await AnOrderWaitingForCash(theirs);

        Result<OrderStatus> result = await CollectAsync(mine, order.Code.Value);

        Assert.Equal(CashierErrors.OrderNotFound, result.Error);

        await using DrinkItDbContext check = sql.CreateContext(theirs.Id);
        Assert.Equal(OrderStatus.AwaitingPayment, (await check.Orders.SingleAsync(row => row.Id == order.Id)).Status);
    }

    // The QR on the customer's phone carries the token, not the code.
    [Fact]
    public async Task FindByTokenAsync_WhenTheTokenIsThisVenues_FindsTheOrder()
    {
        Venue venue = await ASeededVenue();
        Order order = await AnOrderWaitingForCash(venue);

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        CashierOrder? found = await new CashierQueries(context).FindByTokenAsync(order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(order.Code.Value, found?.Code);
    }

    [Fact]
    public async Task FindByTokenAsync_WhenTheTokenIsAnotherVenues_FindsNothing()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Order order = await AnOrderWaitingForCash(theirs);

        await using DrinkItDbContext context = sql.CreateContext(mine.Id);

        Assert.Null(await new CashierQueries(context).FindByTokenAsync(order.TrackingToken.Value, CancellationToken.None));
    }

    // "Cobros de tu turno": what this cashier took since the shift started,
    // the latest first — nobody else's, and nothing from before.
    [Fact]
    public async Task GetCollectedByAsync_Always_ListsOnlyWhatThisCashierTookSinceThen()
    {
        Venue venue = await ASeededVenue();
        Order first = await AnOrderWaitingForCash(venue);
        Order second = await AnOrderWaitingForCash(venue);
        Order someoneElses = await AnOrderWaitingForCash(venue);
        Order beforeTheShift = await AnOrderWaitingForCash(venue);
        await CollectAsync(venue, beforeTheShift.Code.Value, at: Tonight.AddHours(-13));
        await CollectAsync(venue, first.Code.Value, at: Tonight);
        await CollectAsync(venue, someoneElses.Code.Value, cashier: "otra.caja");
        await CollectAsync(venue, second.Code.Value, at: Tonight.AddMinutes(5));

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        IReadOnlyList<CashierOrder> mine = await new CashierQueries(context)
            .GetCollectedByAsync("laura.caja", Tonight.AddHours(-12), CancellationToken.None);

        Assert.Equal([second.Code.Value, first.Code.Value], mine.Select(order => order.Code));
    }

    [Fact]
    public async Task GetCollectedByAsync_WhenAnotherVenuesCashierHasTheSameName_ListsNoneOfTheirs()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Order order = await AnOrderWaitingForCash(theirs);
        await CollectAsync(theirs, order.Code.Value);

        await using DrinkItDbContext context = sql.CreateContext(mine.Id);

        Assert.Empty(await new CashierQueries(context).GetCollectedByAsync("laura.caja", Tonight.AddHours(-12), CancellationToken.None));
    }

    private async Task<Result<OrderStatus>> CollectAsync(
        Venue venue,
        string code,
        SpyDispatcher? spy = null,
        DateTimeOffset? at = null,
        string cashier = "laura.caja")
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        return await new CollectCashHandler(
                new OrderRepository(context, spy ?? new SpyDispatcher(), NullLogger<OrderRepository>.Instance),
                new BarsForOrdering(context),
                new FixedClock(at ?? Tonight),
                new TheCashierIs(cashier))
            .HandleAsync(code, CancellationToken.None);
    }

    /// <summary>A venue selling gin tonics tonight, with one bar holding some of them.</summary>
    private async Task<Venue> ASeededVenue(int barUnits = 20)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);
        StaffUser bar = StaffUser.Create(venue.Id, "barra.pista", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, "laura.caja", "hash", StaffRole.Cashier);
        Night tonight = Night.Create(venue.Id, "Tonight", Tonight.AddHours(-3), Tonight.AddHours(4), [bar, till]);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        seed.StaffUsers.AddRange(bar, till);
        seed.Nights.Add(tonight);
        seed.BarStocks.Add(new BarStock { VenueId = venue.Id, NightId = tonight.Id, KdsId = bar.Id, ProductId = gin.Id, Units = barUnits });
        await seed.SaveChangesAsync();

        _seeded[venue.Id] = new Seeded(tonight.Id, bar.Id, gin.Id);

        return venue;
    }

    private async Task<Order> AnOrderWaitingForCash(Venue venue)
    {
        Seeded seeded = _seeded[venue.Id];
        Order order = Order.Place(
            venue.Id,
            seeded.NightId,
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(seeded.GinId, "Gin Tonic", 4500m, 2, "sin hielo")]);
        order.AwaitPayment(PaymentMethod.Cash);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return order;
    }

    private sealed record Seeded(Guid NightId, Guid BarId, Guid GinId);

    private sealed class TheKdsIs(Guid id) : ICurrentStaffUser
    {
        public string? Username => "barra.pista";

        public Guid? Id => id;
    }

    private sealed class TheCashierIs(string username) : ICurrentStaffUser
    {
        public string? Username => username;

        public Guid? Id => null;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SpyDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Received { get; } = [];

        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            Received.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
