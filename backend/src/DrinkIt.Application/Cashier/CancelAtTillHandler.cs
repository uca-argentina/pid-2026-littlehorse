using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Cashier;

/// <summary>
/// US-23: the customer left without paying, and the cashier cancels the order
/// that was waiting for their cash. Its drinks go back on the shelf.
/// </summary>
/// <remarks>
/// Like collecting, anything but an order waiting for cash is answered, not
/// thrown: it is an ordinary night at the till. Already canceled — the
/// customer did it from the phone — is simply done.
/// </remarks>
public sealed class CancelAtTillHandler(IOrderRepository orders)
{
    public async Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return CashierErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        if (order is null) return CashierErrors.OrderNotFound;
        if (order.Status == OrderStatus.Canceled) return order.Status;
        if (order.Status != OrderStatus.AwaitingPayment) return CashierErrors.AlreadyPaid;

        order.Cancel();

        Result<Order> saved = await orders.SaveCancellationAsync(order, cancellationToken);

        // Another till collected it in the same instant and got there first:
        // for this till it is paid, and there is nothing to cancel.
        if (!saved.IsSuccess) return CashierErrors.AlreadyPaid;

        return order.Status;
    }
}
