using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Payments;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Payments;

/// <summary>
/// US-24: what moves an order that waits for Mercado Pago — the payment's
/// answer, read back from the gateway; the customer coming back without
/// paying; and the fifteen minutes running out.
/// </summary>
public class UnpaidOrderHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 1, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ApprovedAt = Now.AddMinutes(-1);

    private static Order AnOrderAwaitingPayment(string code = "K-4821")
    {
        Order order = Order.Place(
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse(code),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 2, null)]);
        order.AwaitPayment(PaymentMethod.Digital);

        return order;
    }

    private static GatewayPayment APayment(Order order, GatewayPaymentStatus status) =>
        new("1234567890", order.Id, status, status == GatewayPaymentStatus.Approved ? ApprovedAt : null);

    public class ApplyingAPayment
    {
        private static async Task<Result<OrderStatus>> Apply(UnpaidOrdersInMemory orders, GatewayPayment? payment) =>
            await new ApplyPaymentHandler(new GatewayThatKnows(payment), orders, new FixedClock(Now))
                .HandleAsync("1234567890", CancellationToken.None);

        // Approved: paid at the moment the gateway approved it, and off to the bar.
        [Fact]
        public async Task HandleAsync_WhenApproved_PaysTheOrderAndQueuesIt()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);

            Result<OrderStatus> result = await Apply(orders, APayment(order, GatewayPaymentStatus.Approved));

            Assert.Equal(OrderStatus.Queued, result.Value);
            Assert.Equal(ApprovedAt, order.PaidAt);
            Assert.Same(order, orders.Saved.Single());
        }

        // The customer's return and the gateway's notification both say so:
        // the second one changes nothing and writes nothing.
        [Fact]
        public async Task HandleAsync_WhenApprovedAgain_ChangesNothing()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);
            await Apply(orders, APayment(order, GatewayPaymentStatus.Approved));

            Result<OrderStatus> again = await Apply(orders, APayment(order, GatewayPaymentStatus.Approved));

            Assert.Equal(OrderStatus.Queued, again.Value);
            Assert.Single(orders.Saved);
        }

        [Fact]
        public async Task HandleAsync_WhenRejected_CancelsTheOrderAndGivesItsStockBack()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);

            Result<OrderStatus> result = await Apply(orders, APayment(order, GatewayPaymentStatus.Rejected));

            Assert.Equal(OrderStatus.Canceled, result.Value);
            Assert.Same(order, orders.StockReturned.Single());
        }

        // A card rejected on the gateway's page and then another one approved:
        // the late news of the first attempt must not cancel a paid order.
        [Fact]
        public async Task HandleAsync_WhenRejectedAfterItWasPaid_ChangesNothing()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);
            await Apply(orders, APayment(order, GatewayPaymentStatus.Approved));

            Result<OrderStatus> result = await Apply(orders, APayment(order, GatewayPaymentStatus.Rejected));

            Assert.Equal(OrderStatus.Queued, result.Value);
            Assert.Empty(orders.StockReturned);
        }

        // Under review, or cash to be paid at a Rapipago: it keeps waiting.
        [Fact]
        public async Task HandleAsync_WhenPending_LeavesItWaiting()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);

            Result<OrderStatus> result = await Apply(orders, APayment(order, GatewayPaymentStatus.Pending));

            Assert.Equal(OrderStatus.AwaitingPayment, result.Value);
            Assert.Empty(orders.Saved);
        }

        /// <summary>
        /// Money came in for an order that was already canceled — paid on the
        /// gateway's page right as the fifteen minutes ran out. The order stays
        /// canceled, and the answer says so, so it can be refunded by hand.
        /// </summary>
        [Fact]
        public async Task HandleAsync_WhenApprovedForACanceledOrder_LeavesItCanceledAndSaysMoneyCameIn()
        {
            Order order = AnOrderAwaitingPayment();
            order.CancelUnpaid();

            Result<OrderStatus> result = await Apply(new UnpaidOrdersInMemory(order), APayment(order, GatewayPaymentStatus.Approved));

            Assert.Equal(PaymentErrors.PaidAfterCancel, result.Error);
            Assert.Equal(OrderStatus.Canceled, order.Status);
        }

        [Fact]
        public async Task HandleAsync_WhenTheGatewayKnowsNoSuchPayment_ReturnsUnknownPayment()
        {
            Result<OrderStatus> result = await Apply(new UnpaidOrdersInMemory(AnOrderAwaitingPayment()), payment: null);

            Assert.Equal(PaymentErrors.UnknownPayment, result.Error);
        }

        // The notification came through another venue's address: this venue's
        // orders do not have it, exactly as the global query filter would say.
        [Fact]
        public async Task HandleAsync_WhenThePaymentIsForAnOrderThisVenueDoesNotHave_ReturnsUnknownPayment()
        {
            Order elsewhere = AnOrderAwaitingPayment();

            Result<OrderStatus> result = await Apply(new UnpaidOrdersInMemory(), APayment(elsewhere, GatewayPaymentStatus.Approved));

            Assert.Equal(PaymentErrors.UnknownPayment, result.Error);
        }
    }

    public class ComingBackFromThePaymentPage
    {
        private static Task<Result<OrderStatus>> ComeBack(
            UnpaidOrdersInMemory orders,
            Order order,
            string? paymentId = null,
            GatewayPayment? known = null,
            string? token = null) =>
            new ReturnFromPaymentHandler(new GatewayThatKnows(known), orders, new FixedClock(Now))
                .HandleAsync(order.Code.Value, token ?? order.TrackingToken.Value, paymentId, CancellationToken.None);

        // Decided on 2026-09-30: back from the gateway without paying cancels
        // it on the spot, and its drinks go back on the menu.
        [Fact]
        public async Task HandleAsync_WithoutAPayment_CancelsItAndGivesItsStockBack()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);

            Result<OrderStatus> result = await ComeBack(orders, order);

            Assert.Equal(OrderStatus.Canceled, result.Value);
            Assert.Same(order, orders.StockReturned.Single());
        }

        // Paid on the gateway's page: the return moves it, before or without the notification.
        [Fact]
        public async Task HandleAsync_WithAnApprovedPaymentOfThisOrder_PaysAndQueuesIt()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);

            Result<OrderStatus> result = await ComeBack(orders, order, "1234567890", APayment(order, GatewayPaymentStatus.Approved));

            Assert.Equal(OrderStatus.Queued, result.Value);
        }

        [Fact]
        public async Task HandleAsync_WithAPendingPayment_LeavesItWaiting()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);

            Result<OrderStatus> result = await ComeBack(orders, order, "1234567890", APayment(order, GatewayPaymentStatus.Pending));

            Assert.Equal(OrderStatus.AwaitingPayment, result.Value);
            Assert.Empty(orders.StockReturned);
        }

        // The payment id comes from the address, which the customer controls:
        // somebody else's approved payment does not pay this order.
        [Fact]
        public async Task HandleAsync_WithAPaymentOfAnotherOrder_ReturnsUnknownPaymentAndChangesNothing()
        {
            Order order = AnOrderAwaitingPayment();
            Order another = AnOrderAwaitingPayment(code: "K-0066");
            UnpaidOrdersInMemory orders = new(order, another);

            Result<OrderStatus> result = await ComeBack(orders, order, "1234567890", APayment(another, GatewayPaymentStatus.Approved));

            Assert.Equal(PaymentErrors.UnknownPayment, result.Error);
            Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
            Assert.Equal(OrderStatus.AwaitingPayment, another.Status);
        }

        // The notification got there first and it is paid: coming back does not undo that.
        [Fact]
        public async Task HandleAsync_WithoutAPaymentWhenItWasAlreadyPaid_LeavesItPaid()
        {
            Order order = AnOrderAwaitingPayment();
            order.Pay(ApprovedAt, PaymentMethod.Digital);
            order.Enqueue();
            UnpaidOrdersInMemory orders = new(order);

            Result<OrderStatus> result = await ComeBack(orders, order);

            Assert.Equal(OrderStatus.Queued, result.Value);
            Assert.Empty(orders.StockReturned);
        }

        // Only whoever holds the order's link can give up on it.
        [Fact]
        public async Task HandleAsync_WhenTheTokenIsNotTheOrders_ReturnsNotFoundAndChangesNothing()
        {
            Order order = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(order);

            Result<OrderStatus> result = await ComeBack(orders, order, token: TrackingToken.New().Value);

            Assert.Equal(PaymentErrors.OrderNotFound, result.Error);
            Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        }
    }

    public class RunningOutOfTime
    {
        [Fact]
        public async Task HandleAsync_Always_CancelsWhatWaitedLongerThanTheWindowAndGivesItsStockBack()
        {
            Order stale = AnOrderAwaitingPayment();
            Order fresh = AnOrderAwaitingPayment();
            UnpaidOrdersInMemory orders = new(stale, fresh);
            orders.PlacedAt[stale.Id] = Now - DigitalPaymentWindow - TimeSpan.FromSeconds(1);
            orders.PlacedAt[fresh.Id] = Now - DigitalPaymentWindow + TimeSpan.FromSeconds(1);

            int canceled = await new ExpireUnpaidOrdersHandler(orders, new FixedClock(Now))
                .HandleAsync(CancellationToken.None);

            Assert.Equal(1, canceled);
            Assert.Equal(OrderStatus.Canceled, stale.Status);
            Assert.Equal(OrderStatus.AwaitingPayment, fresh.Status);
            Assert.Same(stale, orders.StockReturned.Single());
        }

        private static TimeSpan DigitalPaymentWindow => DigitalPaymentStrategy.PaymentWindow;
    }

    private sealed class GatewayThatKnows(GatewayPayment? payment) : IPaymentGateway
    {
        public Task<GatewayPayment?> FindPaymentAsync(string paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(payment?.Id == paymentId ? payment : null);

        public Task<Result<string>> StartCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Applying a payment never opens a checkout.");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
