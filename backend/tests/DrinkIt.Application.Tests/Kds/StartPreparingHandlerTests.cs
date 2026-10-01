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
        Order order = Order.Place(
            TheVenue,
            "María Quadro",
            OrderCode.Parse(code),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);
        order.Enqueue();

        return order;
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsQueued_MovesItToInPreparation()
    {
        Order order = AQueuedOrder();

        Result<OrderStatus> result = await new StartPreparingHandler(new KdsOrdersInMemory(order))
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.InPreparation, result.Value);
        Assert.Equal(OrderStatus.InPreparation, order.Status);
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsQueued_SavesIt()
    {
        KdsOrdersInMemory orders = new(AQueuedOrder());

        await new StartPreparingHandler(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(1, orders.Saves);
    }

    // Another tablet moved it in the same instant and got there first.
    [Fact]
    public async Task HandleAsync_WhenAnotherTabletMovedItAtTheSameTime_SaysItChangedMeanwhile()
    {
        KdsOrdersInMemory orders = new(AQueuedOrder());
        orders.RefusesTheNextSave();

        Result<OrderStatus> result = await new StartPreparingHandler(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(OrderErrors.ChangedMeanwhile, result.Error);
    }

    // Another venue's order looks exactly like this too: the filter hides it.
    [Fact]
    public async Task HandleAsync_WhenNoOrderHasThatCode_ReturnsNotFound()
    {
        Result<OrderStatus> result = await new StartPreparingHandler(new KdsOrdersInMemory(AQueuedOrder()))
            .HandleAsync("K-9999", CancellationToken.None);

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
    }

    // Whatever lands in the route, it is not an order of this venue.
    [Fact]
    public async Task HandleAsync_WhenTheCodeIsMalformed_ReturnsNotFound()
    {
        Result<OrderStatus> result = await new StartPreparingHandler(new KdsOrdersInMemory(AQueuedOrder()))
            .HandleAsync("not-a-code", CancellationToken.None);

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
    }
}
