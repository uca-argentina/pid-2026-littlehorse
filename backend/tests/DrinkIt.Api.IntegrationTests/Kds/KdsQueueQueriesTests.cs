using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Kds;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Kds;

/// <summary>
/// US-15: what the bar's tablet reads, and the one invariant that matters more
/// than any of it — a boliche never sees another's queue.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class KdsQueueQueriesTests(SqlServerFixture sql)
{
    private static readonly Guid Gin = Guid.CreateVersion7();

    [Fact]
    public async Task GetQueueAsync_WhenOrdersArePending_ReturnsThemOldestPaidFirst()
    {
        Venue venue = await ASeededVenue();

        Order newer = await AnOrderQueuedAt(venue, "Nico", new DateTimeOffset(2026, 9, 27, 23, 10, 0, TimeSpan.Zero));
        Order older = await AnOrderQueuedAt(venue, "Euge", new DateTimeOffset(2026, 9, 27, 23, 0, 0, TimeSpan.Zero));

        IReadOnlyList<KdsQueueOrder> queue = await Queries(venue).GetQueueAsync(CancellationToken.None);

        Assert.Equal([older.Code.Value, newer.Code.Value], queue.Select(order => order.Code));
    }

    [Fact]
    public async Task GetQueueAsync_WhenAnOrderIsQueued_CarriesItsDrinksAndWhoItIsFor()
    {
        Venue venue = await ASeededVenue();
        Order order = await AnOrderQueuedAt(venue, "María Quadro", DateTimeOffset.UtcNow, quantity: 2, note: "sin hielo");

        KdsQueueOrder found = (await Queries(venue).GetQueueAsync(CancellationToken.None)).Single();

        Assert.Equal(order.Code.Value, found.Code);
        Assert.Equal("María Quadro", found.CustomerName);
        Assert.Equal(OrderStatus.Queued, found.Status);
        KdsQueueOrderItem item = found.OrderItems.Single();
        Assert.Equal("Gin Tonic", item.ProductName);
        Assert.Equal(2, item.Quantity);
        Assert.Equal("sin hielo", item.Note);
    }

    [Theory]
    [InlineData(OrderStatus.InPreparation)]
    [InlineData(OrderStatus.Ready)]
    public async Task GetQueueAsync_WhenTheBarIsAlreadyOnIt_StillShowsIt(OrderStatus status)
    {
        Venue venue = await ASeededVenue();
        Order order = await AnOrderQueuedAt(venue, "María Quadro", DateTimeOffset.UtcNow);

        await MoveTo(venue, order, status);

        KdsQueueOrder found = (await Queries(venue).GetQueueAsync(CancellationToken.None)).Single();

        Assert.Equal(status, found.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Canceled)]
    public async Task GetQueueAsync_WhenNobodyIsWaitingOnItAnyMore_ExcludesIt(OrderStatus status)
    {
        Venue venue = await ASeededVenue();
        Order order = await AnOrderQueuedAt(venue, "María Quadro", DateTimeOffset.UtcNow);

        await MoveTo(venue, order, status);

        Assert.Empty(await Queries(venue).GetQueueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetQueueAsync_WhenPaidFromTheVipBalance_MarksItForTheTable()
    {
        Venue venue = await ASeededVenue();
        await AnOrderQueuedAt(venue, "Mesa 5", DateTimeOffset.UtcNow, method: PaymentMethod.VipBalance);

        KdsQueueOrder found = (await Queries(venue).GetQueueAsync(CancellationToken.None)).Single();

        Assert.True(found.IsForTable);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Digital)]
    public async Task GetQueueAsync_WhenPaidAtTheBar_DoesNotMarkItForTheTable(PaymentMethod method)
    {
        Venue venue = await ASeededVenue();
        await AnOrderQueuedAt(venue, "María Quadro", DateTimeOffset.UtcNow, method: method);

        KdsQueueOrder found = (await Queries(venue).GetQueueAsync(CancellationToken.None)).Single();

        Assert.False(found.IsForTable);
    }

    // The invariant CLAUDE.md asks for on everything that touches Order: one
    // boliche's tablet must never draw another's queue.
    [Fact]
    public async Task GetQueueAsync_WhenAnotherVenueHasOrdersPending_ExcludesThem()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        await AnOrderQueuedAt(theirs, "Otra Persona", DateTimeOffset.UtcNow);

        Assert.Empty(await Queries(mine).GetQueueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetQueueAsync_WhenNothingIsWaiting_ReturnsEmpty()
    {
        Venue venue = await ASeededVenue();

        Assert.Empty(await Queries(venue).GetQueueAsync(CancellationToken.None));
    }

    private KdsQueueQueries Queries(Venue venue) => new(sql.CreateContext(venue.Id));

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

    private async Task<Order> AnOrderQueuedAt(
        Venue venue,
        string customer,
        DateTimeOffset paidAt,
        int quantity = 1,
        string? note = null,
        PaymentMethod method = PaymentMethod.Digital)
    {
        Order order = Order.Place(
            venue.Id,
            customer,
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(Gin, "Gin Tonic", 4500m, quantity, note)]);

        order.Pay(paidAt, method);
        order.Enqueue();

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return order;
    }
}
