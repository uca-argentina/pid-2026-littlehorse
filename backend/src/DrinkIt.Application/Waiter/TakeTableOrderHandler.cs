using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Waiter;

/// <summary>What the waiter's phone needs once the order is theirs.</summary>
public sealed record TakenTableOrder(Guid OrderId, int TableNumber, string WaiterName, DateTimeOffset TakenAt);

/// <summary>
/// US-43, criteria 2 and 3: "La llevo yo". Nobody assigns tables (§8.3): the
/// first waiter to tap has it, and the second finds out whose it is.
/// </summary>
public sealed class TakeTableOrderHandler(
    IOrderRepository orders,
    IWaiterQueries waiters,
    ICurrentStaffUser waiter,
    TimeProvider clock)
{
    public async Task<Result<TakenTableOrder>> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        // The waiter is the token's, never the request's: nobody takes a table
        // in somebody else's name by sending another id.
        if (waiter.Id is not Guid waiterId) return WaiterErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(orderId, cancellationToken);

        // Another venue's, one that never existed and one picked up at the bar
        // all answer the same: a waiter has no business telling them apart.
        if (order?.TableId is null) return WaiterErrors.OrderNotFound;

        if (order.WaiterId is Guid current && current != waiterId) return WaiterErrors.AlreadyTaken;

        string name = await waiters.GetMyNameAsync(cancellationToken);

        // Still being made, or already delivered: the domain refuses, as ever.
        order.TakeToTable(waiterId, name, clock.GetUtcNow());

        // Another waiter saved in the same instant and got there first: theirs
        // stands, and this screen reloads to see whose it is.
        Result<Order> saved = await orders.SaveAsync(order, cancellationToken);

        if (!saved.IsSuccess) return saved.Error!;

        return new TakenTableOrder(order.Id, order.TableNumber!.Value, order.WaiterName!, order.TakenAt!.Value);
    }
}
