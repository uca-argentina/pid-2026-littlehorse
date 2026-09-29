using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>
/// What every button on the bar's board does to an order: find it by the code
/// on its card, move it, save it. Written once so each use case is only the
/// move it makes.
/// </summary>
internal static class KdsOrderMove
{
    /// <summary>
    /// The order's new status, or <see cref="KdsErrors.OrderNotFound"/> for a
    /// code that is not one of this venue's orders — malformed ones included.
    /// A move the order cannot make throws from the domain, as ever.
    /// </summary>
    public static async Task<Result<OrderStatus>> ApplyAsync(
        IOrderRepository orders,
        string code,
        Action<Order> move,
        CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return KdsErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        if (order is null) return KdsErrors.OrderNotFound;

        move(order);

        await orders.SaveAsync(order, cancellationToken);

        return order.Status;
    }
}
