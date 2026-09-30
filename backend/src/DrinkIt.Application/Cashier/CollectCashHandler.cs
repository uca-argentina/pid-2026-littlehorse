using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Cashier;

/// <summary>
/// US-26: the cashier took the money for the order behind this code. Paid now,
/// and handed to the bar — saving it raises what puts it on the board.
/// </summary>
/// <remarks>
/// Collecting what is not waiting for money is answered, not thrown: a second
/// tap, or a customer showing the same code twice, is an ordinary night at the
/// till (criterion 3), and the screen has to say so in words.
/// </remarks>
public sealed class CollectCashHandler(IOrderRepository orders, TimeProvider clock, ICurrentStaffUser cashier)
{
    public async Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return CashierErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        if (order is null) return CashierErrors.OrderNotFound;
        if (order.Status != OrderStatus.AwaitingPayment) return CashierErrors.AlreadyPaid;

        // The till's route demands a signed-in cashier, so there is always a
        // name here; the domain refuses the order rather than a blank one.
        order.CollectCash(clock.GetUtcNow(), cashier.Username ?? string.Empty);

        await orders.SaveAsync(order, cancellationToken);

        return order.Status;
    }
}
