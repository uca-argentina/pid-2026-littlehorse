using DrinkIt.Application.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// US-23: the customer changed their mind before paying at the till, and
/// cancels from their own tracking screen. Its drinks go back on the shelf.
/// </summary>
/// <remarks>
/// Proven theirs the same way the tracking screen is: the code finds the
/// order, and the token has to match it, compared in constant time. Every way
/// of not getting in answers <see cref="OrderErrors.NotFound"/>, so a refusal
/// says nothing about which codes exist.
/// </remarks>
public sealed class CancelOrderHandler(IOrderRepository orders)
{
    public async Task<Result<OrderStatus>> HandleAsync(string code, string token, CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return OrderErrors.NotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        if (order is null || !order.TrackingToken.Matches(token)) return OrderErrors.NotFound;
        if (order.Status == OrderStatus.Canceled) return order.Status;
        if (order.Status != OrderStatus.AwaitingPayment) return OrderErrors.NotCancelable;

        order.Cancel();

        Result<Order> saved = await orders.SaveCancellationAsync(order, cancellationToken);

        // The till collected it in the same instant and got there first: for
        // the customer it is paid, the same answer CancelAtTillHandler gives.
        return saved.IsSuccess ? order.Status : OrderErrors.NotCancelable;
    }
}
