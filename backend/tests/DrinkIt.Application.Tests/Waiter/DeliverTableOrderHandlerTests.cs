using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Tests.Kds;
using DrinkIt.Application.Waiter;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Waiter;

public class DeliverTableOrderHandlerTests
{
    private static readonly Guid Martin = Guid.CreateVersion7();

    private static readonly Guid Sofia = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);

    private static Order AReadyTableOrder(Guid? takenBy = null)
    {
        Order order = Order.Place(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "María Quadro", OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500, 2, null)]);
        order.ServeAtTable(Guid.CreateVersion7(), 5);
        order.PayFromBalance(Now, order.Total);
        order.Enqueue(TestBars.Main);
        order.StartPreparing(Now);
        order.MarkReady(Now);

        if (takenBy is Guid waiter) order.TakeToTable(waiter, "Quien sea", Now);

        order.ClearDomainEvents();

        return order;
    }

    private static DeliverTableOrderHandler HandlerFor(KdsOrdersInMemory orders, Guid waiter) =>
        new(orders, new TheWaiterIs(waiter), new Clock());

    // Criterion 1.
    [Fact]
    public async Task HandleAsync_WhenTheWaiterCarriesIt_DeliversItAndSaves()
    {
        Order order = AReadyTableOrder(takenBy: Martin);
        KdsOrdersInMemory orders = new(order);

        Result<DeliveredTableOrder> result = await HandlerFor(orders, Martin).HandleAsync(order.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(Now, order.DeliveredAt);
        Assert.Equal(1, orders.Saves);
        Assert.Equal(5, result.Value.TableNumber);
    }

    // Criterion 2: another waiter's, and one nobody took, are not theirs.
    [Fact]
    public async Task HandleAsync_WhenAnotherWaiterCarriesIt_FailsNotYoursAndLeavesItAlone()
    {
        Order order = AReadyTableOrder(takenBy: Sofia);
        KdsOrdersInMemory orders = new(order);

        Result<DeliveredTableOrder> result = await HandlerFor(orders, Martin).HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal(WaiterErrors.NotYours, result.Error);
        Assert.Equal(OrderStatus.Ready, order.Status);
        Assert.Equal(0, orders.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenNobodyTookIt_FailsNotYours()
    {
        Order order = AReadyTableOrder();

        Result<DeliveredTableOrder> result = await HandlerFor(new KdsOrdersInMemory(order), Martin)
            .HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal(WaiterErrors.NotYours, result.Error);
        Assert.Equal(OrderStatus.Ready, order.Status);
    }

    [Fact]
    public async Task HandleAsync_WhenThereIsNoSuchOrder_FailsOrderNotFound()
    {
        Result<DeliveredTableOrder> result = await HandlerFor(new KdsOrdersInMemory(), Martin)
            .HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(WaiterErrors.OrderNotFound, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherScreenSavesFirst_FailsChangedMeanwhile()
    {
        Order order = AReadyTableOrder(takenBy: Martin);
        KdsOrdersInMemory orders = new(order);
        orders.RefusesTheNextSave();

        Result<DeliveredTableOrder> result = await HandlerFor(orders, Martin).HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderErrors.ChangedMeanwhile, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheBarSentItBackAfterTheTake_FailsNotYoursSoAStaleTapIsAConflict()
    {
        Order order = AReadyTableOrder(takenBy: Martin);
        order.ReturnToPreparation();

        // Sent back, it forgot the waiter: the handler answers NotYours before
        // the domain is asked, so a stale tap is a conflict and not a 400.
        Result<DeliveredTableOrder> result = await HandlerFor(new KdsOrdersInMemory(order), Martin)
            .HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal(WaiterErrors.NotYours, result.Error);
    }

    private sealed class TheWaiterIs(Guid id) : ICurrentStaffUser
    {
        public string? Username => "martin.p";

        public Guid? Id => id;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
