using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Waiter;

/// <summary>Which table's order was handed over.</summary>
public sealed record DeliveredTableOrder(Guid OrderId, int TableNumber);

/// <summary>
/// US-44: "Entregado". The waiter who took the table's order leaves it at the
/// table and closes it, so it leaves everybody's list. It is the bar's own
/// <c>Order.Deliver</c>; what is new is who may do it.
/// </summary>
public sealed class DeliverTableOrderHandler(IOrderRepository orders, ICurrentStaffUser waiter, TimeProvider clock)
{
    public async Task<Result<DeliveredTableOrder>> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // The waiter is the token's, never the request's.
        if (waiter.Id is not Guid waiterId) return WaiterErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(orderId, cancellationToken);

        // Another venue's, one that never existed and one picked up at the bar
        // all answer the same: a waiter has no business telling them apart.
        if (order?.TableId is null) return WaiterErrors.OrderNotFound;

        // Criterion 2. Also what a stale tap finds when the bar sent it back
        // after the take: it forgot the waiter, so it is a conflict, not a 400.
        if (order.WaiterId != waiterId) return WaiterErrors.NotYours;

        order.Deliver(clock.GetUtcNow());

        // Another screen saved in the same instant and got there first: theirs
        // stands, and this phone reloads with it.
        Result<Order> saved = await orders.SaveAsync(order, cancellationToken);

        if (!saved.IsSuccess) return saved.Error!;

        return new DeliveredTableOrder(order.Id, order.TableNumber!.Value);
    }
}
