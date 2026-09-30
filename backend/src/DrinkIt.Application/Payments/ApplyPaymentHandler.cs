using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Payments;

/// <summary>
/// US-24: the gateway's notification that a payment changed moves the order
/// that waits for it.
/// </summary>
/// <remarks>
/// The payment is read back from the gateway by its id, never taken from the
/// notification's body: a forged "approved" is worth nothing.
/// </remarks>
public sealed class ApplyPaymentHandler(IPaymentGateway gateway, IOrderRepository orders, TimeProvider clock)
{
    public async Task<Result<OrderStatus>> HandleAsync(string paymentId, CancellationToken cancellationToken)
    {
        GatewayPayment? payment = await gateway.FindPaymentAsync(paymentId, cancellationToken);

        if (payment is null) return PaymentErrors.UnknownPayment;

        Order? order = await orders.GetForUpdateAsync(payment.OrderId, cancellationToken);

        if (order is null) return PaymentErrors.UnknownPayment;

        return await UnpaidOrderMoves.ApplyAsync(order, payment, orders, clock, cancellationToken);
    }
}
