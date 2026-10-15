using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Waiter;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Waiter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Waiter;

/// <summary>
/// US-43 against a real database: taking a table's order is actually written,
/// the others hear of it only once it is, and two waiters tapping at once end
/// with exactly one of them carrying it (criterion 3).
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class TakeTableOrderTests(SqlServerFixture sql)
{
    private static readonly Guid TheBar = Guid.CreateVersion7();

    private static readonly Guid Gin = Guid.CreateVersion7();

    private static readonly DateTimeOffset Midnight = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WhenTheTableOrderIsFree_WritesWhoTookItAndTellsTheWaiters()
    {
        (Venue venue, StaffUser martin, Order order) = await AReadyTableOrderWithAWaiter("martin.p", "Martín");
        SpyDispatcher spy = new();

        Result<TakenTableOrder> result;
        await using (DrinkItDbContext context = sql.CreateContext(venue.Id))
        {
            result = await HandlerFor(context, martin, spy).HandleAsync(order.Id, CancellationToken.None);
        }

        Assert.True(result.IsSuccess);
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Order stored = await check.Orders.SingleAsync(row => row.Id == order.Id);
        Assert.Equal(martin.Id, stored.WaiterId);
        Assert.Equal("Martín", stored.WaiterName);
        Assert.NotNull(stored.TakenAt);
        TableOrderChanged raised = Assert.IsType<TableOrderChanged>(Assert.Single(spy.Received));
        Assert.Equal(venue.Id, raised.VenueId);
    }

    // The invariant CLAUDE.md asks for on everything that touches Order.
    [Fact]
    public async Task HandleAsync_WhenTheOrderBelongsToAnotherVenue_FindsNothingAndChangesNothing()
    {
        (Venue mine, StaffUser martin, _) = await AReadyTableOrderWithAWaiter("martin.p", "Martín");
        (Venue theirs, _, Order foreign) = await AReadyTableOrderWithAWaiter("sofia.t", "Sofía");

        Result<TakenTableOrder> result;
        await using (DrinkItDbContext context = sql.CreateContext(mine.Id))
        {
            result = await HandlerFor(context, martin, new SpyDispatcher()).HandleAsync(foreign.Id, CancellationToken.None);
        }

        Assert.Equal(WaiterErrors.OrderNotFound, result.Error);
        await using DrinkItDbContext check = sql.CreateContext(theirs.Id);
        Assert.Null((await check.Orders.SingleAsync(row => row.Id == foreign.Id)).WaiterId);
    }

    // Criterion 3, the real race: both phones loaded it free, both tap, one
    // write lands first. The other is refused, and the order stays the first's.
    [Fact]
    public async Task SaveAsync_WhenTwoWaitersTakeItAtOnce_OnlyTheFirstOneStands()
    {
        (Venue venue, StaffUser martin, Order order) = await AReadyTableOrderWithAWaiter("martin.p", "Martín");
        StaffUser sofia = await AWaiter(venue, "sofia.t", "Sofía");

        await using DrinkItDbContext martinsContext = sql.CreateContext(venue.Id);
        await using DrinkItDbContext sofiasContext = sql.CreateContext(venue.Id);
        OrderRepository martins = Repository(martinsContext, new SpyDispatcher());
        OrderRepository sofias = Repository(sofiasContext, new SpyDispatcher());

        Order loadedByMartin = (await martins.GetForUpdateAsync(order.Id, CancellationToken.None))!;
        Order loadedBySofia = (await sofias.GetForUpdateAsync(order.Id, CancellationToken.None))!;
        loadedByMartin.TakeToTable(martin.Id, "Martín", Midnight);
        loadedBySofia.TakeToTable(sofia.Id, "Sofía", Midnight.AddSeconds(1));

        Result<Order> first = await martins.SaveAsync(loadedByMartin, CancellationToken.None);
        Result<Order> second = await sofias.SaveAsync(loadedBySofia, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(OrderErrors.ChangedMeanwhile, second.Error);
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Order stored = await check.Orders.SingleAsync(row => row.Id == order.Id);
        Assert.Equal(martin.Id, stored.WaiterId);
        Assert.Equal("Martín", stored.WaiterName);
    }

    // The second to ask, once the first has saved, is told whose it is.
    [Fact]
    public async Task HandleAsync_WhenAnotherWaiterAlreadyTookIt_FailsAlreadyTaken()
    {
        (Venue venue, StaffUser martin, Order order) = await AReadyTableOrderWithAWaiter("martin.p", "Martín");
        StaffUser sofia = await AWaiter(venue, "sofia.t", "Sofía");

        await using (DrinkItDbContext first = sql.CreateContext(venue.Id))
        {
            await HandlerFor(first, martin, new SpyDispatcher()).HandleAsync(order.Id, CancellationToken.None);
        }

        Result<TakenTableOrder> second;
        await using (DrinkItDbContext context = sql.CreateContext(venue.Id))
        {
            second = await HandlerFor(context, sofia, new SpyDispatcher()).HandleAsync(order.Id, CancellationToken.None);
        }

        Assert.Equal(WaiterErrors.AlreadyTaken, second.Error);
    }

    private static TakeTableOrderHandler HandlerFor(DrinkItDbContext context, StaffUser waiter, SpyDispatcher spy) =>
        new(Repository(context, spy), new WaiterQueries(context, new TheWaiterIs(waiter.Id)), new TheWaiterIs(waiter.Id), TimeProvider.System);

    private static OrderRepository Repository(DrinkItDbContext context, SpyDispatcher spy) =>
        new(context, spy, NullLogger<OrderRepository>.Instance);

    private async Task<(Venue Venue, StaffUser Waiter, Order Order)> AReadyTableOrderWithAWaiter(string username, string displayName)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);
        Table table = Table.Create(venue.Id, 5, 8, null, DateTimeOffset.UtcNow);
        StaffUser waiter = StaffUser.Create(venue.Id, username, "hash", StaffRole.Waiter, displayName);

        Order order = Order.Place(venue.Id, Guid.CreateVersion7(), "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(Gin, "Gin Tonic", 4500m, 1, null)]);
        order.ServeAtTable(table.Id, table.Number);
        order.PayFromBalance(Midnight, order.Total);
        order.Enqueue(TheBar);
        order.StartPreparing(Midnight);
        order.MarkReady(Midnight);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        seed.Tables.Add(table);
        seed.StaffUsers.Add(waiter);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return (venue, waiter, order);
    }

    private async Task<StaffUser> AWaiter(Venue venue, string username, string displayName)
    {
        StaffUser waiter = StaffUser.Create(venue.Id, username, "hash", StaffRole.Waiter, displayName);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.StaffUsers.Add(waiter);
        await seed.SaveChangesAsync();

        return waiter;
    }

    private sealed class TheWaiterIs(Guid id) : ICurrentStaffUser
    {
        public string? Username => "waiter";

        public Guid? Id => id;
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
