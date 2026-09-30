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
/// US-23: an order waiting for cash is canceled, and the drinks it took off
/// the shelf when it was confirmed go back on it — in the same unit of work,
/// or not at all.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class OrderCancellationTests(SqlServerFixture sql)
{
    /// <summary>What is left on the shelf once the order below took its three.</summary>
    private const int LeftAfterTheOrder = 17;

    private const int TakenByTheOrder = 3;

    [Fact]
    public async Task SaveCancellationAsync_WhenTheOrderAwaitsPayment_CancelsItAndPutsItsDrinksBack()
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
        Assert.Equal(LeftAfterTheOrder + TakenByTheOrder, (await check.Products.SingleAsync(row => row.Id == gin.Id)).Stock);
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
        collecting.CollectCash(DateTimeOffset.UtcNow, "laura.caja");
        canceling.Cancel();

        Assert.True((await till.SaveAsync(collecting, CancellationToken.None)).IsSuccess);
        Result<Order> lost = await phone.SaveCancellationAsync(canceling, CancellationToken.None);

        Assert.Equal(OrderErrors.ChangedMeanwhile, lost.Error);
        Assert.Empty(phoneSpy.Received);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Assert.Equal(OrderStatus.Queued, (await check.Orders.SingleAsync(row => row.Id == waiting.Id)).Status);
        Assert.Equal(LeftAfterTheOrder, (await check.Products.SingleAsync(row => row.Id == gin.Id)).Stock);
    }

    private static OrderRepository Repository(DrinkItDbContext context, SpyDispatcher spy) =>
        new(context, spy, NullLogger<OrderRepository>.Instance);

    /// <summary>
    /// A venue with a gin whose stock already has this order's three taken
    /// off, as confirming the order left it.
    /// </summary>
    private async Task<(Venue Venue, Product Gin, Order Order)> AnOrderWaitingForCash()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, LeftAfterTheOrder, category.Id);

        Order order = Order.Place(
            venue.Id,
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(gin.Id, "Gin Tonic", 4500m, TakenByTheOrder, null)]);
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
