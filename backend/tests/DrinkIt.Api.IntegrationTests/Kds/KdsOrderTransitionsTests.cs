using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Kds;

/// <summary>
/// US-16 against a real database: taking an order and handing it back are
/// actually written, the board hears about it only once they are, and one
/// boliche's tablet cannot move another's order.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class KdsOrderTransitionsTests(SqlServerFixture sql)
{
    private static readonly Guid Gin = Guid.CreateVersion7();

    private static readonly DateTimeOffset PaidAt = new(2026, 9, 28, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartPreparing_WhenTheOrderIsQueued_StoresItInPreparation()
    {
        Venue venue = await ASeededVenue();
        Order order = await AQueuedOrder(venue);

        await TakeAsync(venue, order.Code.Value);

        Assert.Equal(OrderStatus.InPreparation, await StatusOf(venue, order));
    }

    [Fact]
    public async Task StartPreparing_WhenSaved_NotifiesTheBoardOfItsVenue()
    {
        Venue venue = await ASeededVenue();
        Order order = await AQueuedOrder(venue);
        SpyDispatcher spy = new();

        await TakeAsync(venue, order.Code.Value, spy);

        OrderPreparationStarted raised = Assert.IsType<OrderPreparationStarted>(Assert.Single(spy.Received));
        Assert.Equal(venue.Id, raised.VenueId);
    }

    // The invariant CLAUDE.md asks for on everything that touches Order.
    [Fact]
    public async Task StartPreparing_WhenTheOrderBelongsToAnotherVenue_FindsNothingAndChangesNothing()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Order order = await AQueuedOrder(theirs);

        Result<OrderStatus> result = await TakeAsync(mine, order.Code.Value);

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
        Assert.Equal(OrderStatus.Queued, await StatusOf(theirs, order));
    }

    // Criterion 4: back among Nuevos, as old as it was.
    [Fact]
    public async Task ReturnToQueue_WhenTheOrderIsInPreparation_StoresItQueuedWithItsOriginalAge()
    {
        Venue venue = await ASeededVenue();
        Order order = await AQueuedOrder(venue);
        await TakeAsync(venue, order.Code.Value);

        await using (DrinkItDbContext context = sql.CreateContext(venue.Id))
        {
            await new ReturnToQueueHandler(Repository(context, new SpyDispatcher()))
                .HandleAsync(order.Code.Value, CancellationToken.None);
        }

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Order stored = await check.Orders.SingleAsync(row => row.Id == order.Id);
        Assert.Equal(OrderStatus.Queued, stored.Status);
        Assert.Equal(PaidAt, stored.PaidAt);
    }

    private static readonly DateTimeOffset Tonight = PaidAt.AddMinutes(8);

    // Decided on 2026-09-28: each column's clock is the order's audit stamp
    // of its last change, written on save with who made it — the station.
    [Fact]
    public async Task StartPreparing_WhenTaken_StampsWhenAndWhichStation()
    {
        Venue venue = await ASeededVenue();
        Order order = await AQueuedOrder(venue);

        await AsTheStationAt(venue, Tonight, context =>
            new StartPreparingHandler(Repository(context, new SpyDispatcher())).HandleAsync(order.Code.Value, CancellationToken.None));

        Order stored = await StoredAsync(venue, order);
        Assert.Equal(Tonight, stored.LastModifiedAt);
        Assert.Equal("barra.demo", stored.LastModifiedBy);
    }

    // Moving to the next column starts its clock again.
    [Fact]
    public async Task MarkReady_WhenInPreparation_RestartsTheClock()
    {
        Venue venue = await ASeededVenue();
        Order order = await AQueuedOrder(venue);
        DateTimeOffset later = Tonight.AddMinutes(4);

        await AsTheStationAt(venue, Tonight, context =>
            new StartPreparingHandler(Repository(context, new SpyDispatcher())).HandleAsync(order.Code.Value, CancellationToken.None));
        await AsTheStationAt(venue, later, context =>
            new MarkReadyHandler(Repository(context, new SpyDispatcher())).HandleAsync(order.Code.Value, CancellationToken.None));

        Order stored = await StoredAsync(venue, order);
        Assert.Equal(OrderStatus.Ready, stored.Status);
        Assert.Equal(later, stored.LastModifiedAt);
    }

    // A double tap changes nothing, so nothing is saved and the clock keeps
    // counting from when it really moved.
    [Fact]
    public async Task StartPreparing_WhenTappedTwice_KeepsTheFirstMoment()
    {
        Venue venue = await ASeededVenue();
        Order order = await AQueuedOrder(venue);

        await AsTheStationAt(venue, Tonight, context =>
            new StartPreparingHandler(Repository(context, new SpyDispatcher())).HandleAsync(order.Code.Value, CancellationToken.None));
        await AsTheStationAt(venue, Tonight.AddMinutes(3), context =>
            new StartPreparingHandler(Repository(context, new SpyDispatcher())).HandleAsync(order.Code.Value, CancellationToken.None));

        Assert.Equal(Tonight, (await StoredAsync(venue, order)).LastModifiedAt);
    }

    [Fact]
    public async Task MarkReady_WhenTheOrderBelongsToAnotherVenue_FindsNothingAndChangesNothing()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Order order = await AQueuedOrder(theirs);
        await TakeAsync(theirs, order.Code.Value);

        Result<OrderStatus> result = await MoveAsync(mine, context =>
            new MarkReadyHandler(Repository(context, new SpyDispatcher()))
                .HandleAsync(order.Code.Value, CancellationToken.None));

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
        Assert.Equal(OrderStatus.InPreparation, await StatusOf(theirs, order));
    }

    // Delivered leaves the board: the queue no longer carries it.
    [Fact]
    public async Task Deliver_WhenReady_StoresItDeliveredAndTakesItOffTheQueue()
    {
        Venue venue = await ASeededVenue();
        Order order = await AReadyOrder(venue);

        await MoveAsync(venue, context => new DeliverHandler(Repository(context, new SpyDispatcher()), new FixedClock(Tonight))
            .HandleAsync(order.Code.Value, CancellationToken.None));

        Assert.Equal(Tonight, (await StoredAsync(venue, order)).DeliveredAt);
        await using DrinkItDbContext queue = sql.CreateContext(venue.Id);
        Assert.Empty(await new DrinkIt.Infrastructure.Kds.KdsQueueQueries(queue).GetQueueAsync(CancellationToken.None));
    }

    // US-20: the QR on the customer's phone carries the token, and the bar
    // finds the order by it — through the value conversion, in SQL.
    [Fact]
    public async Task Scan_WhenTheOrderIsReady_StoresItDelivered()
    {
        Venue venue = await ASeededVenue();
        Order order = await AReadyOrder(venue);

        Result<ScannedOrder> result = await ScanAsync(venue, order.TrackingToken.Value);

        Assert.Equal(ScanOutcome.Delivered, result.Value.Outcome);
        Order stored = await StoredAsync(venue, order);
        Assert.Equal(OrderStatus.Delivered, stored.Status);
        Assert.Equal(Tonight, stored.DeliveredAt);
    }

    // The invariant CLAUDE.md asks for, now by token: another venue's QR reads
    // exactly like a stranger's.
    [Fact]
    public async Task Scan_WhenTheOrderBelongsToAnotherVenue_ReturnsUnknownCodeAndChangesNothing()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Order order = await AReadyOrder(theirs);

        Result<ScannedOrder> result = await ScanAsync(mine, order.TrackingToken.Value);

        Assert.Equal(KdsErrors.UnknownCode, result.Error);
        Assert.Equal(OrderStatus.Ready, await StatusOf(theirs, order));
    }

    // The scan hands over the one order a token leads to. Two of a venue's
    // orders sharing one would make that a coin toss, so the database refuses
    // it rather than trusting 128 random bits alone.
    [Fact]
    public async Task Save_WhenTwoOrdersOfOneVenueShareATrackingToken_IsRefused()
    {
        Venue venue = await ASeededVenue();
        Order first = await AQueuedOrder(venue);
        Order second = await AQueuedOrder(venue);

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        // Straight to SQL, past SaveChanges, so it arrives unwrapped: 2601 is
        // SQL Server refusing a duplicate in a unique index.
        SqlException error = await Assert.ThrowsAsync<SqlException>(() => context.Orders
            .Where(row => row.Id == second.Id)
            .ExecuteUpdateAsync(row => row.SetProperty(o => o.TrackingToken, first.TrackingToken)));

        Assert.Equal(2601, error.Number);
    }

    private async Task<Result<ScannedOrder>> ScanAsync(Venue venue, string read)
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        return await new ScanHandler(Repository(context, new SpyDispatcher()), new FixedClock(Tonight))
            .HandleAsync(read, CancellationToken.None);
    }

    // The 58 orders a demo marked delivered by hand have no moment. An undo
    // that cannot be timed must not reopen one of them.
    [Fact]
    public async Task UndoDelivery_WhenNoMomentWasRecorded_RefusesIt()
    {
        Venue venue = await ASeededVenue();
        Order order = await AQueuedOrder(venue);

        await using (DrinkItDbContext moving = sql.CreateContext(venue.Id))
        {
            await moving.Orders
                .Where(row => row.Id == order.Id)
                .ExecuteUpdateAsync(row => row.SetProperty(o => o.Status, OrderStatus.Delivered));
        }

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => MoveAsync(venue, context =>
            new UndoDeliveryHandler(Repository(context, new SpyDispatcher()), new FixedClock(Tonight))
                .HandleAsync(order.Code.Value, CancellationToken.None)));

        Assert.Equal(Order.ErrorCodes.UndoWindowPassed, error.Code);
        Assert.Equal(OrderStatus.Delivered, await StatusOf(venue, order));
    }

    /// <summary>With the audit stamp a real request registers, at a known moment, as the station.</summary>
    private async Task<Result<OrderStatus>> AsTheStationAt(
        Venue venue,
        DateTimeOffset at,
        Func<DrinkItDbContext, Task<Result<OrderStatus>>> move)
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id, new AuditInterceptor(new FixedClock(at), new Station()));

        return await move(context);
    }

    private sealed class Station : ICurrentStaffUser
    {
        public string? Username => "barra.demo";
    }

    private async Task<Result<OrderStatus>> MoveAsync(Venue venue, Func<DrinkItDbContext, Task<Result<OrderStatus>>> move)
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        return await move(context);
    }

    private async Task<Order> AReadyOrder(Venue venue)
    {
        Order order = await AQueuedOrder(venue);
        await TakeAsync(venue, order.Code.Value);
        await MoveAsync(venue, context => new MarkReadyHandler(Repository(context, new SpyDispatcher()))
            .HandleAsync(order.Code.Value, CancellationToken.None));

        return order;
    }

    private async Task<Order> StoredAsync(Venue venue, Order order)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return await check.Orders.SingleAsync(row => row.Id == order.Id);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<Result<OrderStatus>> TakeAsync(Venue venue, string code, SpyDispatcher? spy = null)
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        return await new StartPreparingHandler(Repository(context, spy ?? new SpyDispatcher()))
            .HandleAsync(code, CancellationToken.None);
    }

    private static OrderRepository Repository(DrinkItDbContext context, SpyDispatcher spy) =>
        new(context, spy, NullLogger<OrderRepository>.Instance);

    private async Task<OrderStatus> StatusOf(Venue venue, Order order)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return (await check.Orders.SingleAsync(row => row.Id == order.Id)).Status;
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

    private async Task<Order> AQueuedOrder(Venue venue)
    {
        Order order = Order.Place(
            venue.Id,
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(Gin, "Gin Tonic", 4500m, 1, null)]);
        order.Pay(PaidAt, PaymentMethod.Digital);
        order.Enqueue();

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return order;
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
