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
/// Two screens changing the same order in the same instant: two tills
/// collecting it, two tablets moving it. Both read it in the same state and
/// both pass the domain's checks in memory, so only the database can tell
/// which one got there first — and the other must be told, not overwritten.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class OrderConcurrencyTests(SqlServerFixture sql)
{
    private static readonly Guid Gin = Guid.CreateVersion7();

    private static readonly DateTimeOffset Tonight = new(2026, 9, 30, 1, 12, 0, TimeSpan.Zero);

    // The one that costs money: the customer must not be charged by both.
    [Fact]
    public async Task SaveAsync_WhenAnotherTillCollectedItFirst_RefusesAndKeepsTheFirstCollection()
    {
        Venue venue = await ASeededVenue();
        Order waiting = await AnOrderWaitingForCash(venue);

        await using DrinkItDbContext first = sql.CreateContext(venue.Id);
        await using DrinkItDbContext second = sql.CreateContext(venue.Id);
        SpyDispatcher secondSpy = new();
        OrderRepository firstTill = Repository(first, new SpyDispatcher());
        OrderRepository secondTill = Repository(second, secondSpy);

        Order atTheFirstTill = (await firstTill.GetForUpdateAsync(waiting.Code, CancellationToken.None))!;
        Order atTheSecondTill = (await secondTill.GetForUpdateAsync(waiting.Code, CancellationToken.None))!;
        atTheFirstTill.CollectCash(Tonight, "caja.uno");
        atTheSecondTill.CollectCash(Tonight.AddSeconds(1), "caja.dos");

        Result<Order> won = await firstTill.SaveAsync(atTheFirstTill, CancellationToken.None);
        Result<Order> lost = await secondTill.SaveAsync(atTheSecondTill, CancellationToken.None);

        Assert.True(won.IsSuccess);
        Assert.Equal(OrderErrors.ChangedMeanwhile, lost.Error);
        Assert.Empty(secondSpy.Received);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Order stored = await check.Orders.SingleAsync(row => row.Id == waiting.Id);
        Assert.Equal("caja.uno", stored.CollectedBy);
        Assert.Equal(Tonight, stored.PaidAt);
    }

    // Two tablets: one marks it ready, the other hands it back to the queue.
    [Fact]
    public async Task SaveAsync_WhenAnotherTabletMovedItFirst_RefusesAndKeepsTheFirstMove()
    {
        Venue venue = await ASeededVenue();
        Order taken = await AnOrderInPreparation(venue);

        await using DrinkItDbContext first = sql.CreateContext(venue.Id);
        await using DrinkItDbContext second = sql.CreateContext(venue.Id);
        OrderRepository firstTablet = Repository(first, new SpyDispatcher());
        OrderRepository secondTablet = Repository(second, new SpyDispatcher());

        Order onTheFirst = (await firstTablet.GetForUpdateAsync(taken.Code, CancellationToken.None))!;
        Order onTheSecond = (await secondTablet.GetForUpdateAsync(taken.Code, CancellationToken.None))!;
        onTheFirst.MarkReady();
        onTheSecond.ReturnToQueue();

        Assert.True((await firstTablet.SaveAsync(onTheFirst, CancellationToken.None)).IsSuccess);
        Assert.Equal(OrderErrors.ChangedMeanwhile, (await secondTablet.SaveAsync(onTheSecond, CancellationToken.None)).Error);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Assert.Equal(OrderStatus.Ready, (await check.Orders.SingleAsync(row => row.Id == taken.Id)).Status);
    }

    // One after the other is not a race: the second one reads the new state.
    [Fact]
    public async Task SaveAsync_WhenNobodyElseChangedIt_SavesEveryTime()
    {
        Venue venue = await ASeededVenue();
        Order taken = await AnOrderInPreparation(venue);

        for (int round = 0; round < 2; round++)
        {
            await using DrinkItDbContext context = sql.CreateContext(venue.Id);
            OrderRepository tablet = Repository(context, new SpyDispatcher());
            Order order = (await tablet.GetForUpdateAsync(taken.Code, CancellationToken.None))!;

            if (round == 0) order.MarkReady();
            else order.ReturnToPreparation();

            Assert.True((await tablet.SaveAsync(order, CancellationToken.None)).IsSuccess);
        }
    }

    private static OrderRepository Repository(DrinkItDbContext context, SpyDispatcher spy) =>
        new(context, spy, NullLogger<OrderRepository>.Instance);

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

    private Task<Order> AnOrderWaitingForCash(Venue venue) =>
        Stored(venue, order => order.AwaitPayment(PaymentMethod.Cash));

    private Task<Order> AnOrderInPreparation(Venue venue) =>
        Stored(venue, order =>
        {
            order.Pay(Tonight, PaymentMethod.Digital);
            order.Enqueue();
            order.StartPreparing();
        });

    private async Task<Order> Stored(Venue venue, Action<Order> prepare)
    {
        Order order = Order.Place(
            venue.Id,
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(Gin, "Gin Tonic", 4500m, 1, null)]);
        prepare(order);
        order.ClearDomainEvents();

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
