using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
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
        Assert.Equal(LeftAfterTheOrder + TakenByTheOrder, await RemainingOf(check, gin));
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
        Assert.Equal(LeftAfterTheOrder, await RemainingOf(check, gin));
    }

    /// <summary>
    /// The drinks go back to the night the order was placed in, not to
    /// whichever one is on when the cancellation arrives.
    /// </summary>
    [Fact]
    public async Task SaveCancellationAsync_WhenAnotherNightHasStockOfTheSameDrink_ReturnsItsDrinksToTheOrdersNight()
    {
        (Venue venue, Product gin, Order waiting) = await AnOrderWaitingForCash();
        Guid otherNight = await ALaterNightWithItsOwnStockOf(venue, gin, units: 5);

        await using (DrinkItDbContext context = sql.CreateContext(venue.Id))
        {
            OrderRepository till = Repository(context, new SpyDispatcher());
            Order order = (await till.GetForUpdateAsync(waiting.Code, CancellationToken.None))!;
            order.Cancel();

            await till.SaveCancellationAsync(order, CancellationToken.None);
        }

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        Assert.Equal(LeftAfterTheOrder + TakenByTheOrder, await RemainingOf(check, gin, waiting.NightId!.Value));
        Assert.Equal(5, await RemainingOf(check, gin, otherNight));
    }

    private static Task<int> RemainingOf(DrinkItDbContext check, Product product) =>
        check.NightStocks
            .Where(stock => stock.ProductId == product.Id)
            .OrderBy(stock => stock.Loaded)
            .Select(stock => stock.Remaining)
            .FirstAsync();

    private static Task<int> RemainingOf(DrinkItDbContext check, Product product, Guid night) =>
        check.NightStocks
            .Where(stock => stock.ProductId == product.Id && stock.NightId == night)
            .Select(stock => stock.Remaining)
            .SingleAsync();

    private async Task<Guid> ALaterNightWithItsOwnStockOf(Venue venue, Product gin, int units)
    {
        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        StaffUser kds = StaffUser.Create(venue.Id, $"kds-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier);
        Night later = Night.Create(venue.Id, "Later", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(7), [kds, till]);
        seed.StaffUsers.AddRange(kds, till);
        seed.Nights.Add(later);
        seed.NightStocks.Add(NightStock.Open(venue.Id, later.Id, gin.Id, units));
        await seed.SaveChangesAsync();

        return later.Id;
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
        StaffUser kds = StaffUser.Create(venue.Id, $"kds-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier);
        Night night = Night.Create(venue.Id, "Tonight", DateTimeOffset.UtcNow.AddHours(-3), DateTimeOffset.UtcNow.AddHours(4), [kds, till]);

        Order order = Order.Place(
            venue.Id,
            night.Id,
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(gin.Id, "Gin Tonic", 4500m, TakenByTheOrder, null)]);
        order.AwaitPayment(PaymentMethod.Cash);
        order.ClearDomainEvents();

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        seed.StaffUsers.AddRange(kds, till);
        seed.Nights.Add(night);
        // The night had twenty and the order took its three, as confirming it left it.
        seed.NightStocks.Add(NightStock.Open(venue.Id, night.Id, gin.Id, LeftAfterTheOrder + TakenByTheOrder));
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        await seed.NightStocks
            .Where(stock => stock.NightId == night.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(stock => stock.Remaining, LeftAfterTheOrder));

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
