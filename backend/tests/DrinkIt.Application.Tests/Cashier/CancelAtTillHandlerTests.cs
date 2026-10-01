using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Tests.Kds;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Cashier;

/// <summary>
/// US-23: the customer left without paying, and the cashier cancels the order
/// that was waiting for their cash — its drinks go back on the shelf.
/// </summary>
public class CancelAtTillHandlerTests
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

        Result<OrderStatus> result = await new CancelAtTillHandler(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Equal(1, orders.Cancellations);
    }

    [Fact]
    public async Task HandleAsync_WhenNoOrderHasThatCode_ReturnsNotFound()
    {
        Result<OrderStatus> result = await new CancelAtTillHandler(new KdsOrdersInMemory(AnOrderWaitingForCash()))
            .HandleAsync("K-9999", CancellationToken.None);

        Assert.Equal(CashierErrors.OrderNotFound, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCodeIsNotACode_ReturnsNotFound()
    {
        Result<OrderStatus> result = await new CancelAtTillHandler(new KdsOrdersInMemory(AnOrderWaitingForCash()))
            .HandleAsync("not a code", CancellationToken.None);

        Assert.Equal(CashierErrors.OrderNotFound, result.Error);
    }

    // Paid is the bar's: the till says so in words, it does not throw.
    [Fact]
    public async Task HandleAsync_WhenTheOrderWasAlreadyPaid_RefusesWithoutSaving()
    {
        Order order = AnOrderWaitingForCash();
        order.CollectCash(DateTimeOffset.UtcNow, "laura.caja");
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new CancelAtTillHandler(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(CashierErrors.AlreadyPaid, result.Error);
        Assert.Equal(0, orders.Cancellations);
    }

    // The customer canceled it from the phone a moment before: nothing left
    // to do, and nothing to put back twice.
    [Fact]
    public async Task HandleAsync_WhenTheOrderWasAlreadyCanceled_SucceedsWithoutSaving()
    {
        Order order = AnOrderWaitingForCash();
        order.Cancel();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await new CancelAtTillHandler(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(OrderStatus.Canceled, result.Value);
        Assert.Equal(0, orders.Cancellations);
    }

    // Another till collected it in the same instant and got there first.
    [Fact]
    public async Task HandleAsync_WhenSomebodyElseChangedItMeanwhile_RefusesAsAlreadyPaid()
    {
        KdsOrdersInMemory orders = new(AnOrderWaitingForCash());
        orders.RefusesTheNextSave();

        Result<OrderStatus> result = await new CancelAtTillHandler(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(CashierErrors.AlreadyPaid, result.Error);
    }
}
