using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Kds;

/// <summary>
/// US-18: the rest of the bar's buttons — Listo, Volver a preparación,
/// Entregado and its Deshacer — each one an order found by its code, moved,
/// and saved, at the moment the clock says.
/// </summary>
public class ReadyAndDeliveredHandlersTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 2, 0, 0, TimeSpan.Zero);

    private static Order AnOrderInPreparation()
    {
        Order order = Order.Place(
            TheVenue,
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.Pay(Now.AddMinutes(-10), PaymentMethod.Digital);
        order.Enqueue();
        order.StartPreparing();

        return order;
    }

    private static Order AReadyOrder()
    {
        Order order = AnOrderInPreparation();
        order.MarkReady();

        return order;
    }

    [Fact]
    public async Task MarkReady_HandleAsync_WhenInPreparation_MarksItReadyAndSavesIt()
    {
        Order order = AnOrderInPreparation();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new MarkReadyHandler(orders)
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(OrderStatus.Ready, result.Value);
        Assert.Equal(1, orders.Saves);
    }

    [Fact]
    public async Task MarkReady_HandleAsync_WhenNoOrderHasThatCode_ReturnsNotFound()
    {
        Result<OrderStatus> result = await new MarkReadyHandler(new KdsOrdersInMemory())
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(KdsErrors.OrderNotFound, result.Error);
    }

    [Fact]
    public async Task ReturnToPreparation_HandleAsync_WhenReady_PutsItBackAndSavesIt()
    {
        Order order = AReadyOrder();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new ReturnToPreparationHandler(orders)
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(OrderStatus.InPreparation, result.Value);
        Assert.Equal(1, orders.Saves);
    }

    [Fact]
    public async Task Deliver_HandleAsync_WhenReady_DeliversItAtTheClocksMomentAndSavesIt()
    {
        Order order = AReadyOrder();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new DeliverHandler(orders, new FixedClock(Now))
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(OrderStatus.Delivered, result.Value);
        Assert.Equal(Now, order.DeliveredAt);
        Assert.Equal(1, orders.Saves);
    }

    [Fact]
    public async Task UndoDelivery_HandleAsync_RightAfterDelivering_PutsItBackToReady()
    {
        Order order = AReadyOrder();
        order.Deliver(Now);
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new UndoDeliveryHandler(orders, new FixedClock(Now.AddSeconds(8)))
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(OrderStatus.Ready, result.Value);
        Assert.Equal(1, orders.Saves);
    }

    // Past the grace the domain refuses, and nothing is saved.
    [Fact]
    public async Task UndoDelivery_HandleAsync_TooLate_ThrowsAndSavesNothing()
    {
        Order order = AReadyOrder();
        order.Deliver(Now);
        KdsOrdersInMemory orders = new(order);
        UndoDeliveryHandler handler = new(orders, new FixedClock(Now + Order.DeliveryUndoWindow + TimeSpan.FromMinutes(1)));

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => handler.HandleAsync("K-4821", CancellationToken.None));

        Assert.Equal(Order.ErrorCodes.UndoWindowPassed, error.Code);
        Assert.Equal(0, orders.Saves);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
