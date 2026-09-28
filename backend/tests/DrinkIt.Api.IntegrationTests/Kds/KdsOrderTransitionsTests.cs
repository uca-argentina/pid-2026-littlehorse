using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
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
