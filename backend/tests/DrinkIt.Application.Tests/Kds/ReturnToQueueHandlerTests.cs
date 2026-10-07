using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Kds;

/// <summary>US-16, criterion 4: an order taken by mistake goes back to Nuevos.</summary>
public class ReturnToQueueHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static Order AnOrderInPreparation()
    {
        Order order = Order.Place(
            TheVenue,
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);
        order.Enqueue();
        order.StartPreparing();

        return order;
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsInPreparation_PutsItBackInTheQueue()
    {
        Order order = AnOrderInPreparation();

        Result<OrderStatus> result = await new ReturnToQueueHandler(new KdsOrdersInMemory(order))
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Queued, result.Value);
        Assert.Equal(OrderStatus.Queued, order.Status);
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsInPreparation_SavesIt()
    {
        KdsOrdersInMemory orders = new(AnOrderInPreparation());

        await new ReturnToQueueHandler(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(1, orders.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenNoOrderHasThatCode_ReturnsNotFound()
    {
        Result<OrderStatus> result = await new ReturnToQueueHandler(new KdsOrdersInMemory(AnOrderInPreparation()))
            .HandleAsync("K-9999", CancellationToken.None);

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
    }
}
