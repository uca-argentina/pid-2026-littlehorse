using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Tests.Kds;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Orders;

/// <summary>
/// US-23: the customer changed their mind before paying at the till, and
/// cancels from their own tracking screen — proving it is theirs the same way
/// that screen does, with the link's token.
/// </summary>
public class CancelOrderHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static Order AnOrderWaitingForCash()
    {
        Order order = Order.Place(
            TheVenue,
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.AwaitPayment(PaymentMethod.Cash);

        return order;
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderAwaitsPayment_CancelsItAndPutsItsDrinksBack()
    {
        Order order = AnOrderWaitingForCash();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new CancelOrderHandler(orders)
            .HandleAsync("K-4821", order.TrackingToken.Value, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Equal(1, orders.Cancellations);
    }

    // Knowing the code is not owning the order: the codes run in order, and
    // the one next door is this one plus one.
    [Fact]
    public async Task HandleAsync_WhenTheTokenIsNotTheOrders_ReturnsNotFoundWithoutSaving()
    {
        Order order = AnOrderWaitingForCash();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new CancelOrderHandler(orders)
            .HandleAsync("K-4821", TrackingToken.New().Value, CancellationToken.None);

        Assert.Equal(OrderErrors.NotFound, result.Error);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(0, orders.Cancellations);
    }

    [Theory]
    [InlineData("K-9999")]
    [InlineData("not a code")]
    public async Task HandleAsync_WhenTheCodeLeadsNowhere_ReturnsNotFound(string code)
    {
        Order order = AnOrderWaitingForCash();

        Result<OrderStatus> result = await new CancelOrderHandler(new KdsOrdersInMemory(order))
            .HandleAsync(code, order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(OrderErrors.NotFound, result.Error);
    }

    // Paid, by any method, is the bar's: the customer is told, not thrown at.
    [Fact]
    public async Task HandleAsync_WhenTheOrderWasPaid_RefusesAsNotCancelable()
    {
        Order order = AnOrderWaitingForCash();
        order.CollectCash(DateTimeOffset.UtcNow, "laura.caja");
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new CancelOrderHandler(orders)
            .HandleAsync("K-4821", order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(OrderErrors.NotCancelable, result.Error);
        Assert.Equal(0, orders.Cancellations);
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderWasAlreadyCanceled_SucceedsWithoutSaving()
    {
        Order order = AnOrderWaitingForCash();
        order.Cancel();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new CancelOrderHandler(orders)
            .HandleAsync("K-4821", order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(OrderStatus.Canceled, result.Value);
        Assert.Equal(0, orders.Cancellations);
    }

    // The till collected it in the same instant: theirs stands.
    [Fact]
    public async Task HandleAsync_WhenSomebodyElseChangedItMeanwhile_ReturnsChangedMeanwhile()
    {
        Order order = AnOrderWaitingForCash();
        KdsOrdersInMemory orders = new(order);
        orders.RefusesTheNextSave();

        Result<OrderStatus> result = await new CancelOrderHandler(orders)
            .HandleAsync("K-4821", order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(OrderErrors.ChangedMeanwhile, result.Error);
    }
}
