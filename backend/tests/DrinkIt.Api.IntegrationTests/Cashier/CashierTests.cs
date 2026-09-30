using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
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
    private static readonly Guid Gin = Guid.CreateVersion7();

    private static readonly DateTimeOffset Tonight = new(2026, 9, 28, 1, 12, 0, TimeSpan.Zero);

    // US-24, criterion 3.
    [Fact]
    public async Task GetQueueAsync_WhenAnOrderAwaitsCash_LeavesItOffTheBoard()
    {
        Venue venue = await ASeededVenue();
        await AnOrderWaitingForCash(venue);

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        IReadOnlyList<KdsQueueOrder> queue = await new KdsQueueQueries(context).GetQueueAsync(CancellationToken.None);

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
                new FixedClock(at ?? Tonight),
                new TheCashierIs(cashier))
            .HandleAsync(code, CancellationToken.None);
    }

    private async Task<Venue> ASeededVenue()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id));
        await seed.SaveChangesAsync();

        return venue;
    }

    private async Task<Order> AnOrderWaitingForCash(Venue venue)
    {
        Order order = Order.Place(
            venue.Id,
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(Gin, "Gin Tonic", 4500m, 2, "sin hielo")]);
        order.AwaitPayment(PaymentMethod.Cash);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return order;
    }

    private sealed class TheCashierIs(string username) : ICurrentStaffUser
    {
        public string? Username => username;
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
