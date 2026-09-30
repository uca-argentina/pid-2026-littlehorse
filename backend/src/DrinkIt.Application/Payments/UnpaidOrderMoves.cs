using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Payments;

/// <summary>
/// What a payment's answer, or its absence, does to an order that waits for
/// it (US-24). Written once for the gateway's notification and the customer's
/// return, which can both arrive and must agree.
/// </summary>
internal static class UnpaidOrderMoves
{
    /// <summary>
    /// Idempotent: an order that no longer waits is left as it is. Money for
    /// an order already canceled is said out loud, so it can be refunded.
    /// </summary>
    public static async Task<Result<OrderStatus>> ApplyAsync(
        Order order,
        GatewayPayment payment,
        IOrderRepository orders,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            bool moneyForNothing = order.Status == OrderStatus.Canceled && payment.Status == GatewayPaymentStatus.Approved;

            return moneyForNothing ? PaymentErrors.PaidAfterCancel : order.Status;
        }

        switch (payment.Status)
        {
            case GatewayPaymentStatus.Approved:
                order.Pay(payment.ApprovedAt ?? clock.GetUtcNow(), PaymentMethod.Digital);
                order.Enqueue();
                await orders.SaveAsync(order, cancellationToken);
                break;

            case GatewayPaymentStatus.Rejected:
                await CancelAsync(order, orders, cancellationToken);
                break;

            case GatewayPaymentStatus.Pending:
                break;
        }

        return order.Status;
    }

    /// <summary>Nobody paid: canceled, and its drinks go back on the menu in the same write.</summary>
    public static async Task CancelAsync(Order order, IOrderRepository orders, CancellationToken cancellationToken)
    {
        order.CancelUnpaid();
        await orders.SaveReturningStockAsync(order, cancellationToken);
    }
}
