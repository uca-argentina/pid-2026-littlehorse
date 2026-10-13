using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
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
/// US-23: an order waiting for cash is canceled. Since US-36 cash takes its
/// drinks when it is collected, so one still waiting took none, and canceling
/// it puts none back.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class OrderCancellationTests(SqlServerFixture sql)
{
    private static readonly Guid TheBar = Guid.CreateVersion7();

    /// <summary>The gin's stock, which the order waiting for cash has not touched.</summary>
    private const int OnTheShelf = 20;

    [Fact]
    public async Task SaveCancellationAsync_WhenTheOrderAwaitsPayment_CancelsItWithoutMovingStock()
    {
        (Venue venue, Product gin, Order waiting) = await AnOrderWaitingForCash();
        SpyDispatcher spy = new();

        await using (DrinkItDbContext context = sql.CreateContext(venue.Id))
        {
            OrderRepository till = Repository(context, spy);
            Order order = (await till.GetForUpdateAsync(waiting.Code, CancellationToken.None))!;
            order.Cancel();

            Assert.True((await till.SaveCancellationAsync(order, CancellationToken.None)).IsSuccess);
        }

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Assert.Equal(OrderStatus.Canceled, (await check.Orders.SingleAsync(row => row.Id == waiting.Id)).Status);
        Assert.Equal(OnTheShelf, (await check.Products.SingleAsync(row => row.Id == gin.Id)).Stock);
        Assert.IsType<OrderCanceled>(Assert.Single(spy.Received));
    }

    // The till collected it in the same instant: the collection stands, and
    // nothing goes back on the shelf for an order that is still going ahead.
    [Fact]
    public async Task SaveCancellationAsync_WhenTheTillCollectedItFirst_RefusesAndPutsNothingBack()
    {
        (Venue venue, Product gin, Order waiting) = await AnOrderWaitingForCash();

        await using DrinkItDbContext atTheTill = sql.CreateContext(venue.Id);
        await using DrinkItDbContext onThePhone = sql.CreateContext(venue.Id);
        SpyDispatcher phoneSpy = new();
        OrderRepository till = Repository(atTheTill, new SpyDispatcher());
        OrderRepository phone = Repository(onThePhone, phoneSpy);

        Order collecting = (await till.GetForUpdateAsync(waiting.Code, CancellationToken.None))!;
        Order canceling = (await phone.GetForUpdateAsync(waiting.Code, CancellationToken.None))!;
        collecting.CollectCash(DateTimeOffset.UtcNow, "laura.caja", TheBar);
        canceling.Cancel();

        Assert.True((await till.SaveAsync(collecting, CancellationToken.None)).IsSuccess);
        Result<Order> lost = await phone.SaveCancellationAsync(canceling, CancellationToken.None);

        Assert.Equal(OrderErrors.ChangedMeanwhile, lost.Error);
        Assert.Empty(phoneSpy.Received);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Assert.Equal(OrderStatus.Queued, (await check.Orders.SingleAsync(row => row.Id == waiting.Id)).Status);
        Assert.Equal(OnTheShelf, (await check.Products.SingleAsync(row => row.Id == gin.Id)).Stock);
    }

    private static OrderRepository Repository(DrinkItDbContext context, SpyDispatcher spy) =>
        new(context, spy, NullLogger<OrderRepository>.Instance);

    /// <summary>A venue with a gin, and an order of three waiting for cash that took none yet.</summary>
    private async Task<(Venue Venue, Product Gin, Order Order)> AnOrderWaitingForCash()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, OnTheShelf, category.Id);

        Order order = Order.Place(venue.Id, Guid.CreateVersion7(), "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(gin.Id, "Gin Tonic", 4500m, 3, null)]);
        order.AwaitPayment(PaymentMethod.Cash);
        order.ClearDomainEvents();

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return (venue, gin, order);
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
