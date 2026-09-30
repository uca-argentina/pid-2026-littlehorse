using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Payments;

/// <summary>
/// US-24: the customer comes back from Mercado Pago's page to their order.
/// With a payment, its answer moves the order; without one, they gave up, and
/// that cancels it on the spot and puts its drinks back (decided 2026-09-30).
/// </summary>
/// <remarks>
/// Only whoever holds the order's link gets here, the same proof the tracking
/// screen asks for. The payment id comes from the address, which the customer
/// controls: it is read back from the gateway, and it has to be this order's —
/// somebody else's approved payment pays nothing here.
/// </remarks>
public sealed class ReturnFromPaymentHandler(IPaymentGateway gateway, IOrderRepository orders, TimeProvider clock)
{
    public async Task<Result<OrderStatus>> HandleAsync(
        string code,
        string token,
        string? paymentId,
        CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return PaymentErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        if (order is null || !order.TrackingToken.Matches(token)) return PaymentErrors.OrderNotFound;

        if (string.IsNullOrWhiteSpace(paymentId))
        {
            // Paid already — the notification got here first — stays paid.
            if (order.Status == OrderStatus.AwaitingPayment) await UnpaidOrderMoves.CancelAsync(order, orders, cancellationToken);

            return order.Status;
        }

        GatewayPayment? payment = await gateway.FindPaymentAsync(paymentId, cancellationToken);

        if (payment is null || payment.OrderId != order.Id) return PaymentErrors.UnknownPayment;

        return await UnpaidOrderMoves.ApplyAsync(order, payment, orders, clock, cancellationToken);
    }
}
