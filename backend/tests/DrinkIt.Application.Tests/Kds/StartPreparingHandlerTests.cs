using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Kds;

/// <summary>
/// US-16: the bar takes an order off Nuevos by its code — the same code a
/// card shows, and the one each selected card sends on its own.
/// </summary>
public class StartPreparingHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static Order AQueuedOrder(string code = "K-4821")
    {
        Order order = Order.Place(TheVenue, Guid.CreateVersion7(), "María Quadro",
            OrderCode.Parse(code),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);
        order.Enqueue(TestBars.Main);

        return order;
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsQueued_MovesItToInPreparation()
    {
        Order order = AQueuedOrder();

        Result<OrderStatus> result = await new StartPreparingHandler(new KdsOrdersInMemory(order), TimeProvider.System, TheKdsIs.Main)
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.InPreparation, result.Value);
        Assert.Equal(OrderStatus.InPreparation, order.Status);
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsQueued_SavesIt()
    {
        KdsOrdersInMemory orders = new(AQueuedOrder());

        await new StartPreparingHandler(orders, TimeProvider.System, TheKdsIs.Main).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(1, orders.Saves);
    }

    // Another tablet moved it in the same instant and got there first.
    [Fact]
    public async Task HandleAsync_WhenAnotherTabletMovedItAtTheSameTime_SaysItChangedMeanwhile()
    {
        KdsOrdersInMemory orders = new(AQueuedOrder());
        orders.RefusesTheNextSave();

        Result<OrderStatus> result = await new StartPreparingHandler(orders, TimeProvider.System, TheKdsIs.Main).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(OrderErrors.ChangedMeanwhile, result.Error);
    }

    // Another venue's order looks exactly like this too: the filter hides it.
    [Fact]
    public async Task HandleAsync_WhenNoOrderHasThatCode_ReturnsNotFound()
    {
        Result<OrderStatus> result = await new StartPreparingHandler(new KdsOrdersInMemory(AQueuedOrder()), TimeProvider.System, TheKdsIs.Main)
            .HandleAsync("K-9999", CancellationToken.None);

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
    }

    // US-36, criterion 1: another bar's order is not this tablet's to move,
    // and it is told so the same way as a code that does not exist.
    [Fact]
    public async Task HandleAsync_WhenTheOrderWentToAnotherBar_ReturnsNotFoundWithoutMovingIt()
    {
        Order order = AQueuedOrder();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new StartPreparingHandler(orders, TimeProvider.System, new TheKdsIs(TestBars.Other))
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
        Assert.Equal(OrderStatus.Queued, order.Status);
        Assert.Equal(0, orders.Saves);
    }

    // US-36, for US-47: the moment the bar took it, by the server's clock.
    [Fact]
    public async Task HandleAsync_WhenTheOrderIsQueued_StampsWhenThePreparationStarted()
    {
        Order order = AQueuedOrder();
        DateTimeOffset now = new(2026, 10, 12, 1, 30, 0, TimeSpan.Zero);

        await new StartPreparingHandler(new KdsOrdersInMemory(order), new FixedClock(now), TheKdsIs.Main)
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(now, order.PreparationStartedAt);
    }

    // Whatever lands in the route, it is not an order of this venue.
    [Fact]
    public async Task HandleAsync_WhenTheCodeIsMalformed_ReturnsNotFound()
    {
        Result<OrderStatus> result = await new StartPreparingHandler(new KdsOrdersInMemory(AQueuedOrder()), TimeProvider.System, TheKdsIs.Main)
            .HandleAsync("not-a-code", CancellationToken.None);

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
