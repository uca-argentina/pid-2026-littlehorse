using DrinkIt.Api.Features.Payments;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Payments;

/// <summary>
/// US-24: every minute, whatever nobody paid in fifteen is canceled and its
/// drinks go back on the menu — in every venue, each one on its own, behind its
/// own query filter.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class ExpireUnpaidOrdersServiceTests(SqlServerFixture sql) : IAsyncDisposable
{
    private const int StockBefore = 20;

    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task RunOnceAsync_Always_CancelsWhatEachVenueLeftUnpaidTooLongAndGivesItsStockBack()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan window = DigitalPaymentStrategy.PaymentWindow;
        (Venue one, Product oneGin) = await ASeededVenue();
        (Venue other, Product otherGin) = await ASeededVenue();
        Order staleHere = await AnOrderAwaitingPayment(one, oneGin, placedAt: now - window - TimeSpan.FromMinutes(1));
        Order freshHere = await AnOrderAwaitingPayment(one, oneGin, placedAt: now - TimeSpan.FromMinutes(1));
        Order staleThere = await AnOrderAwaitingPayment(other, otherGin, placedAt: now - window - TimeSpan.FromMinutes(1));

        await ExpireUnpaidOrdersService.RunOnceAsync(_factory.Services, CancellationToken.None);

        Assert.Equal(OrderStatus.Canceled, await StatusOf(one, staleHere));
        Assert.Equal(OrderStatus.AwaitingPayment, await StatusOf(one, freshHere));
        Assert.Equal(OrderStatus.Canceled, await StatusOf(other, staleThere));
        Assert.Equal(StockBefore - 1, await StockOf(one, oneGin));
        Assert.Equal(StockBefore, await StockOf(other, otherGin));
    }

    private async Task<Order> AnOrderAwaitingPayment(Venue venue, Product gin, DateTimeOffset placedAt)
    {
        Order order = Order.Place(
            venue.Id,
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(gin.Id, gin.Name, gin.Price, 1, null)]);
        order.AwaitPayment(PaymentMethod.Digital);

        await using DrinkItDbContext context = sql.CreateContext(
            venue.Id,
            new AuditInterceptor(new FixedClock(placedAt), new NoStaff()));

        // Stock taken the way a real order takes it, so giving it back shows.
        await context.Products
            .Where(product => product.Id == gin.Id)
            .ExecuteUpdateAsync(row => row.SetProperty(product => product.Stock, product => product.Stock - 1));
        context.Orders.Add(order);
        context.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await context.SaveChangesAsync();

        return order;
    }

    private async Task<OrderStatus> StatusOf(Venue venue, Order order)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return (await check.Orders.SingleAsync(row => row.Id == order.Id)).Status;
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
}
