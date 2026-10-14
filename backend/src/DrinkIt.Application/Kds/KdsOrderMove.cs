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
    /// code that is not one of this bar's orders (US-36) — another bar's,
    /// another venue's and malformed ones alike —
    /// or <see cref="OrderErrors.ChangedMeanwhile"/> when another tablet moved
    /// it first. A move the order cannot make throws from the domain, as ever.
    /// </summary>
    public static async Task<Result<OrderStatus>> ApplyAsync(
        IOrderRepository orders,
        ICurrentStaffUser kds,
        string code,
        Action<Order> move,
        CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return KdsErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        // The bar comes from the tablet's token, never from the request: a
        // tablet cannot move another bar's order by typing its code.
        if (order is null || order.KdsId != kds.Id) return KdsErrors.OrderNotFound;

        move(order);

        // Another tablet moved it in the same instant and got there first:
        // theirs stands, and the board reloads with it from their notification.
        Result<Order> saved = await orders.SaveAsync(order, cancellationToken);

        return saved.IsSuccess ? order.Status : saved.Error!;
    }
}
