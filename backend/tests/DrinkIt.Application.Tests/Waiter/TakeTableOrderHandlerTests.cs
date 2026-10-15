using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Tests.Kds;
using DrinkIt.Application.Waiter;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Waiter;

public class TakeTableOrderHandlerTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly Guid ANight = Guid.CreateVersion7();

    private static readonly Guid Martin = Guid.CreateVersion7();

    private static readonly Guid Sofia = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);

    private static Order AReadyTableOrder(string code = "K-4821")
    {
        Order order = Order.Place(
            AVenue, ANight, "María Quadro", OrderCode.Parse(code), [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500, 2, null)]);
        order.ServeAtTable(Guid.CreateVersion7(), 5);
        order.PayFromBalance(Now, order.Total);
        order.Enqueue(TestBars.Main);
        order.StartPreparing(Now);
        order.MarkReady(Now);
        order.ClearDomainEvents();

        return order;
    }

    private static Order AReadyBarOrder()
    {
        Order order = Order.Place(
            AVenue, ANight, "María Quadro", OrderCode.Parse("K-4822"), [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500, 1, null)]);
        order.Pay(Now, PaymentMethod.Digital);
        order.Enqueue(TestBars.Main);
        order.StartPreparing(Now);
        order.MarkReady(Now);
        order.ClearDomainEvents();

        return order;
    }

    private static TakeTableOrderHandler HandlerFor(KdsOrdersInMemory orders, Guid waiter, string name = "Martín") =>
        new(orders, new Fake.Waiters(name), new Fake.TheWaiterIs(waiter), new Fake.Clock());

    // Criterion 2.
    [Fact]
    public async Task HandleAsync_WhenTheTableOrderIsFree_MakesItTheirsAndSaves()
    {
        Order order = AReadyTableOrder();
        KdsOrdersInMemory orders = new(order);

        Result<TakenTableOrder> result = await HandlerFor(orders, Martin).HandleAsync(order.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Martin, order.WaiterId);
        Assert.Equal("Martín", order.WaiterName);
        Assert.Equal(Now, order.TakenAt);
        Assert.Equal(1, orders.Saves);
        Assert.Equal(order.Id, result.Value.OrderId);
        Assert.Equal(5, result.Value.TableNumber);
        Assert.Equal("Martín", result.Value.WaiterName);
    }

    // The name is read when it is taken, and by display name when there is one.
    [Fact]
    public async Task HandleAsync_WhenTheWaiterHasADisplayName_KeepsThatName()
    {
        Order order = AReadyTableOrder();

        await HandlerFor(new KdsOrdersInMemory(order), Martin, name: "Martín P.").HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal("Martín P.", order.WaiterName);
    }

    // Criterion 3: the second one finds out whose it is, and nothing is written.
    [Fact]
    public async Task HandleAsync_WhenAnotherWaiterAlreadyHasIt_FailsAndLeavesItWithThem()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Sofia, "Sofía", Now);
        KdsOrdersInMemory orders = new(order);

        Result<TakenTableOrder> result = await HandlerFor(orders, Martin).HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal(WaiterErrors.AlreadyTaken, result.Error);
        Assert.Equal(Sofia, order.WaiterId);
        Assert.Equal(0, orders.Saves);
    }

    // Criterion 3, the real race: both read it free, one saves first.
    [Fact]
    public async Task HandleAsync_WhenAnotherWaiterSavesFirst_FailsChangedMeanwhile()
    {
        Order order = AReadyTableOrder();
        KdsOrdersInMemory orders = new(order);
        orders.RefusesTheNextSave();

        Result<TakenTableOrder> result = await HandlerFor(orders, Martin).HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal(OrderErrors.ChangedMeanwhile, result.Error);
    }

    // A retry after a dropped answer is the same waiter asking again.
    [Fact]
    public async Task HandleAsync_WhenTheSameWaiterAlreadyHasIt_SucceedsWithoutChangingIt()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Martin, "Martín", Now);
        order.ClearDomainEvents();

        Result<TakenTableOrder> result = await HandlerFor(new KdsOrdersInMemory(order), Martin)
            .HandleAsync(order.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, order.TakenAt);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public async Task HandleAsync_WhenThereIsNoSuchOrder_FailsOrderNotFound()
    {
        Result<TakenTableOrder> result = await HandlerFor(new KdsOrdersInMemory(), Martin)
            .HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(WaiterErrors.OrderNotFound, result.Error);
    }

    // Picked up at the bar: not the waiter's, and not something to confirm exists.
    [Fact]
    public async Task HandleAsync_WhenTheOrderIsPickedUpAtTheBar_FailsOrderNotFound()
    {
        Order order = AReadyBarOrder();

        Result<TakenTableOrder> result = await HandlerFor(new KdsOrdersInMemory(order), Martin)
            .HandleAsync(order.Id, CancellationToken.None);

        Assert.Equal(WaiterErrors.OrderNotFound, result.Error);
        Assert.Null(order.WaiterId);
    }

    // A move the order cannot make is the domain's to refuse, as ever.
    [Fact]
    public async Task HandleAsync_WhenTheOrderIsStillBeingMade_ThrowsFromTheDomain()
    {
        Order order = AReadyTableOrder();
        order.ReturnToPreparation();

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => HandlerFor(new KdsOrdersInMemory(order), Martin).HandleAsync(order.Id, CancellationToken.None));

        Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
    }

    private static class Fake
    {
        public sealed class Waiters(string name) : IWaiterQueries
        {
            public Task<IReadOnlyList<WaiterTableOrder>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<WaiterTableOrder>>([]);

            public Task<string> GetMyNameAsync(CancellationToken cancellationToken) => Task.FromResult(name);
        }

        public sealed class TheWaiterIs(Guid id) : ICurrentStaffUser
        {
            public string? Username => "martin.p";

            public Guid? Id => id;
        }

        public sealed class Clock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
