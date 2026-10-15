using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Waiter;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Waiter;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Waiter;

/// <summary>
/// US-43: what a waiter's phone reads, and the invariant that matters more than
/// any of it — a venue's waiters never see another venue's tables.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class WaiterQueriesTests(SqlServerFixture sql)
{
    private static readonly Guid TheBar = Guid.CreateVersion7();

    private static readonly Guid Gin = Guid.CreateVersion7();

    private static readonly DateTimeOffset Midnight = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    // Criterion 1: the one that has waited longest is first.
    [Fact]
    public async Task ListAsync_WhenSeveralTableOrdersAreReady_ReturnsTheOneThatWaitedLongestFirst()
    {
        Venue venue = await ASeededVenue();
        Table five = await ATable(venue, 5);
        Table twelve = await ATable(venue, 12);

        await AReadyTableOrder(venue, five, "Nico", readyAt: Midnight.AddMinutes(10));
        await AReadyTableOrder(venue, twelve, "Sofi", readyAt: Midnight);

        IReadOnlyList<WaiterTableOrder> listed = await Queries(venue).ListAsync(CancellationToken.None);

        Assert.Equal([12, 5], listed.Select(order => order.TableNumber));
    }

    [Fact]
    public async Task ListAsync_WhenATableOrderIsReady_CarriesWhatTheWaiterNeedsToCarryIt()
    {
        Venue venue = await ASeededVenue();
        Table five = await ATable(venue, 5);
        Order order = await AReadyTableOrder(venue, five, "Nico", readyAt: Midnight, quantity: 2, note: "sin hielo");

        WaiterTableOrder found = Assert.Single(await Queries(venue).ListAsync(CancellationToken.None));

        Assert.Equal(order.Id, found.OrderId);
        Assert.Equal(5, found.TableNumber);
        Assert.Equal("Nico", found.CustomerName);
        Assert.Equal(Midnight, found.ReadyAt);
        Assert.Null(found.WaiterId);
        Assert.Null(found.WaiterName);
        WaiterOrderItem item = Assert.Single(found.Items);
        Assert.Equal("Gin Tonic", item.ProductName);
        Assert.Equal(2, item.Quantity);
        Assert.Equal("sin hielo", item.Note);
    }

    // Criterion 2: the others read the name of whoever took it.
    [Fact]
    public async Task ListAsync_WhenAWaiterTookIt_CarriesWhoAndWhen()
    {
        Venue venue = await ASeededVenue();
        Table five = await ATable(venue, 5);
        Guid martin = Guid.CreateVersion7();
        await AReadyTableOrder(venue, five, "Nico", readyAt: Midnight, takenBy: (martin, "Martín"), takenAt: Midnight.AddSeconds(40));

        WaiterTableOrder found = Assert.Single(await Queries(venue).ListAsync(CancellationToken.None));

        Assert.Equal(martin, found.WaiterId);
        Assert.Equal("Martín", found.WaiterName);
        Assert.Equal(Midnight.AddSeconds(40), found.TakenAt);
    }

    // Picked up at the bar is the bar's, and a drink still being made has
    // nothing to carry yet.
    [Fact]
    public async Task ListAsync_WhenTheOrderIsNotAReadyTableOrder_LeavesItOut()
    {
        Venue venue = await ASeededVenue();
        Table five = await ATable(venue, 5);

        await AReadyTableOrder(venue, five, "Nico", readyAt: Midnight, atTheBar: true);
        Order making = await AReadyTableOrder(venue, five, "Sofi", readyAt: Midnight);
        await MoveTo(venue, making, OrderStatus.InPreparation);
        Order delivered = await AReadyTableOrder(venue, five, "Juan", readyAt: Midnight);
        await MoveTo(venue, delivered, OrderStatus.Delivered);

        Assert.Empty(await Queries(venue).ListAsync(CancellationToken.None));
    }

    // The invariant CLAUDE.md asks for on everything that touches Order.
    [Fact]
    public async Task ListAsync_WhenAnotherVenueHasTableOrdersReady_LeavesThemOut()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Table five = await ATable(mine, 5);
        Table alsoFive = await ATable(theirs, 5);

        await AReadyTableOrder(mine, five, "Nico", readyAt: Midnight);
        await AReadyTableOrder(theirs, alsoFive, "Foráneo", readyAt: Midnight);

        WaiterTableOrder found = Assert.Single(await Queries(mine).ListAsync(CancellationToken.None));

        Assert.Equal("Nico", found.CustomerName);
    }

    [Fact]
    public async Task GetMyNameAsync_WhenTheWaiterHasADisplayName_ReturnsIt()
    {
        Venue venue = await ASeededVenue();
        StaffUser martin = await AWaiter(venue, "martin.p", displayName: "Martín");

        string name = await Queries(venue, martin.Id).GetMyNameAsync(CancellationToken.None);

        Assert.Equal("Martín", name);
    }

    [Fact]
    public async Task GetMyNameAsync_WhenTheWaiterHasNoDisplayName_ReturnsTheUsername()
    {
        Venue venue = await ASeededVenue();
        StaffUser martin = await AWaiter(venue, "martin.p", displayName: null);

        string name = await Queries(venue, martin.Id).GetMyNameAsync(CancellationToken.None);

        Assert.Equal("martin.p", name);
    }

    private WaiterQueries Queries(Venue venue, Guid? waiter = null) =>
        new(sql.CreateContext(venue.Id), new TheWaiterIs(waiter ?? Guid.CreateVersion7()));

    private async Task MoveTo(Venue venue, Order order, OrderStatus status)
    {
        await using DrinkItDbContext moving = sql.CreateContext(venue.Id);

        await moving.Orders
            .Where(row => row.Id == order.Id)
            .ExecuteUpdateAsync(row => row.SetProperty(o => o.Status, status));
    }

    private async Task<Venue> ASeededVenue()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        await seed.SaveChangesAsync();

        return venue;
    }

    private async Task<Table> ATable(Venue venue, int number)
    {
        Table table = Table.Create(venue.Id, number, 8, null, DateTimeOffset.UtcNow);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Tables.Add(table);
        await seed.SaveChangesAsync();

        return table;
    }

    private async Task<StaffUser> AWaiter(Venue venue, string username, string? displayName)
    {
        StaffUser waiter = StaffUser.Create(venue.Id, username, "hash", StaffRole.Waiter, displayName);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.StaffUsers.Add(waiter);
        await seed.SaveChangesAsync();

        return waiter;
    }

    private async Task<Order> AReadyTableOrder(
        Venue venue,
        Table table,
        string customer,
        DateTimeOffset readyAt,
        int quantity = 1,
        string? note = null,
        bool atTheBar = false,
        (Guid Id, string Name)? takenBy = null,
        DateTimeOffset? takenAt = null)
    {
        Order order = Order.Place(venue.Id, Guid.CreateVersion7(), customer,
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(Gin, "Gin Tonic", 4500m, quantity, note)]);

        if (atTheBar)
        {
            order.Pay(readyAt, PaymentMethod.Digital);
        }
        else
        {
            order.ServeAtTable(table.Id, table.Number);
            order.PayFromBalance(readyAt, order.Total);
        }

        order.Enqueue(TheBar);
        order.StartPreparing(readyAt);
        order.MarkReady(readyAt);

        if (takenBy is { } who) order.TakeToTable(who.Id, who.Name, takenAt ?? readyAt);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return order;
    }

    private sealed class TheWaiterIs(Guid id) : ICurrentStaffUser
    {
        public string? Username => "martin.p";

        public Guid? Id => id;
    }
}
