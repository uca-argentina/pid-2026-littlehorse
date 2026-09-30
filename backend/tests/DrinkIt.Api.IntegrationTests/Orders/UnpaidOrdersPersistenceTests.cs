using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// US-24 against a real database: an order that waits for Mercado Pago is
/// found by the id the gateway carries back, keeps where it is paid, and — when
/// nobody pays it — gives its drinks back to the menu in the same write that
/// cancels it. One venue never sees another's.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class UnpaidOrdersPersistenceTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Tonight = new(2026, 9, 30, 1, 0, 0, TimeSpan.Zero);

    private const int StockBefore = 20;

    [Fact]
    public async Task GetForUpdateAsync_ById_FindsThisVenuesOrder()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        Order order = await AnOrderAwaitingPayment(venue, gin);

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        Order? found = await Repository(context).GetForUpdateAsync(order.Id, CancellationToken.None);

        Assert.Equal(order.Code, found!.Code);
        Assert.Single(found.Items);
    }

    // The invariant CLAUDE.md asks for: a notification that came through one
    // venue's address never reaches another venue's order.
    [Fact]
    public async Task GetForUpdateAsync_ByIdOfAnotherVenuesOrder_FindsNothing()
    {
        (Venue mine, _) = await ASeededVenue();
        (Venue theirs, Product theirGin) = await ASeededVenue();
        Order order = await AnOrderAwaitingPayment(theirs, theirGin);

        await using DrinkItDbContext context = sql.CreateContext(mine.Id);

        Assert.Null(await Repository(context).GetForUpdateAsync(order.Id, CancellationToken.None));
    }

    // A retry of the same confirmation must find the same checkout: the id
    // Mercado Pago's button opens, and the link a redirect follows.
    [Fact]
    public async Task SaveAsync_WhenACheckoutWasOffered_KeepsIt()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        Order order = await AnOrderAwaitingPayment(venue, gin);
        PaymentCheckout checkout = new("3727754810-123", "https://www.mercadopago.com.ar/checkout/v1/redirect?pref_id=3727754810-123");

        await using (DrinkItDbContext context = sql.CreateContext(venue.Id))
        {
            OrderRepository orders = Repository(context);
            Order tracked = (await orders.GetForUpdateAsync(order.Id, CancellationToken.None))!;
            tracked.OfferCheckout(checkout);
            await orders.SaveAsync(tracked, CancellationToken.None);
        }

        Order stored = await StoredAsync(venue, order);
        Assert.Equal(checkout.Id, stored.PaymentCheckoutId);
        Assert.Equal(checkout.Url, stored.PaymentUrl);
    }

    // Nobody paid, so nothing was sold: the two gin tonics go back on the menu,
    // in the same write that cancels the order.
    [Fact]
    public async Task SaveReturningStockAsync_WhenCanceledUnpaid_CancelsItAndPutsItsDrinksBack()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        Order order = await AnOrderAwaitingPayment(venue, gin, quantity: 2);
        Assert.Equal(StockBefore - 2, await StockOf(venue, gin));

        await using (DrinkItDbContext context = sql.CreateContext(venue.Id))
        {
            OrderRepository orders = Repository(context);
            Order tracked = (await orders.GetForUpdateAsync(order.Id, CancellationToken.None))!;
            tracked.CancelUnpaid();
            await orders.SaveReturningStockAsync(tracked, CancellationToken.None);
        }

        Assert.Equal(OrderStatus.Canceled, (await StoredAsync(venue, order)).Status);
        Assert.Equal(StockBefore, await StockOf(venue, gin));
    }

    [Fact]
    public async Task GetAwaitingPaymentCreatedBeforeAsync_Always_FindsOnlyThisVenuesUnpaidOrdersPlacedBeforeTheMoment()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        (Venue other, Product otherGin) = await ASeededVenue();
        Order stale = await AnOrderAwaitingPayment(venue, gin, placedAt: Tonight.AddMinutes(-20));
        Order fresh = await AnOrderAwaitingPayment(venue, gin, placedAt: Tonight.AddMinutes(-5));
        Order paid = await AnOrderAwaitingPayment(venue, gin, placedAt: Tonight.AddMinutes(-20), pay: true);
        await AnOrderAwaitingPayment(other, otherGin, placedAt: Tonight.AddMinutes(-20));

        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        IReadOnlyList<Order> expired = await Repository(context)
            .GetAwaitingPaymentCreatedBeforeAsync(Tonight.AddMinutes(-15), CancellationToken.None);

        Assert.Equal([stale.Id], expired.Select(order => order.Id));
        Assert.DoesNotContain(fresh.Id, expired.Select(order => order.Id));
        Assert.DoesNotContain(paid.Id, expired.Select(order => order.Id));
    }

    private static OrderRepository Repository(DrinkItDbContext context) =>
        new(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance);

    private async Task<Order> AnOrderAwaitingPayment(
        Venue venue,
        Product gin,
        int quantity = 1,
        DateTimeOffset? placedAt = null,
        bool pay = false)
    {
        Order order = Order.Place(
            venue.Id,
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(gin.Id, gin.Name, gin.Price, quantity, null)]);
        order.AwaitPayment(PaymentMethod.Digital);

        if (pay) order.Pay(Tonight, PaymentMethod.Digital);

        await using DrinkItDbContext context = sql.CreateContext(
            venue.Id,
            new AuditInterceptor(new FixedClock(placedAt ?? Tonight), new NoStaff()));

        Result<Order> added = await Repository(context).AddAsync(order, Guid.NewGuid().ToString(), CancellationToken.None);
        Assert.True(added.IsSuccess);

        return order;
    }

    private async Task<Order> StoredAsync(Venue venue, Order order)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return await check.Orders.SingleAsync(row => row.Id == order.Id);
    }

    private async Task<int> StockOf(Venue venue, Product product)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return (await check.Products.SingleAsync(row => row.Id == product.Id)).Stock;
    }

    private async Task<(Venue Venue, Product Gin)> ASeededVenue()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, StockBefore, category.Id);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        await seed.SaveChangesAsync();

        return (venue, gin);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoStaff : ICurrentStaffUser
    {
        public string? Username => null;
    }

    private sealed class NoDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
