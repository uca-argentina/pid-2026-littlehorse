using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Tests.Kds;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Cashier;

/// <summary>
/// US-26: the cashier looks the order up by the code the customer shows and
/// confirms the money is in. From there it is an order like any other.
/// </summary>
public class CollectCashHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Tonight = new(2026, 9, 28, 1, 12, 0, TimeSpan.Zero);

    private static Order AnOrderWaitingForCash(string code = "K-4821")
    {
        Order order = Order.Place(
            TheVenue,
            "María Quadro",
            OrderCode.Parse(code),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.AwaitPayment(PaymentMethod.Cash);

        return order;
    }

    private static CollectCashHandler AHandlerOver(KdsOrdersInMemory orders) =>
        new(orders, new FixedClock(Tonight), new TheCashierIs("laura.caja"));

    // Criterion 2: paid now, not when the customer confirmed on the phone —
    // the bar counts its age from here — and straight into the bar's queue.
    [Fact]
    public async Task HandleAsync_WhenTheOrderAwaitsPayment_PaysItAndQueuesIt()
    {
        Order order = AnOrderWaitingForCash();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Queued, order.Status);
        Assert.Equal(Tonight, order.PaidAt);
        Assert.Equal("laura.caja", order.CollectedBy);
        Assert.Equal(1, orders.Saves);
    }

    // Criterion 3, and the double tap of the wireframe: collecting twice is
    // refused out loud, never charged twice and never an error page.
    [Fact]
    public async Task HandleAsync_WhenTheOrderWasAlreadyPaid_RefusesWithoutSaving()
    {
        Order order = AnOrderWaitingForCash();
        KdsOrdersInMemory orders = new(order);
        await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Result<OrderStatus> again = await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(CashierErrors.AlreadyPaid, again.Error);
        Assert.Equal(1, orders.Saves);
    }

    // Another venue's order looks exactly like this too: the filter hides it.
    [Theory]
    [InlineData("K-9999")]
    [InlineData("not-a-code")]
    public async Task HandleAsync_WhenNoOrderHasThatCode_ReturnsNotFound(string code)
    {
        Result<OrderStatus> result = await AHandlerOver(new KdsOrdersInMemory(AnOrderWaitingForCash()))
            .HandleAsync(code, CancellationToken.None);

        Assert.Equal(CashierErrors.OrderNotFound, result.Error);
    }

    private sealed class TheCashierIs(string username) : ICurrentStaffUser
    {
        public string? Username => username;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
