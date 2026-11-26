using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;


namespace DrinkIt.Application.Kds;

/// <summary>
/// US-07: turns the switch back on, e.g. after restocking. Does not touch
/// stock, so a product at zero stays sold out until it is restocked too.
/// </summary>
public sealed class MarkOrderNotPriorityHandler(IOrderRepository orders)
{
    public async Task<Result<Order>> HandleAsync(string code, CancellationToken cancellationToken)
    {
        if (!TrackingToken.TryParse(code?.Trim(), out TrackingToken? token)) return KdsErrors.UnknownCode;

        Order? order = await orders.GetForUpdateAsync(token!, cancellationToken);

        if (order is null) return KdsErrors.UnknownCode;

        order.MakeNotPriority();

        await orders.SaveAsync(order, cancellationToken);

        return order;
    }
}
