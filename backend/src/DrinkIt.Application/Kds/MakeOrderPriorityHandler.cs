using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;



namespace DrinkIt.Application.Kds;

/// <summary>
/// US-07: turns the switch back on, e.g. after restocking. Does not touch
/// stock, so a product at zero stays sold out until it is restocked too.
/// </summary>
public sealed class MarkOrderPriorityHandler(IOrderRepository orders)
{
    public async Task<Result<Order>> HandleAsync(string code, CancellationToken cancellationToken)
    {
        // Nothing read is nothing found: a body without it is not worth a 400.
        if (!TrackingToken.TryParse(code?.Trim(), out TrackingToken? token)) return KdsErrors.UnknownCode;

        Order? order = await orders.GetForUpdateAsync(token!, cancellationToken);

        if (order is null) return KdsErrors.UnknownCode;
        // Another bar's order (US-36). One with no bar yet — cash waiting at the
        // till — is not another bar's: it is not ready, which the switch says.

        if (order is null) return OrderErrors.NotFound;

        order.MakePriority();

        await orders.SaveAsync(order, cancellationToken);

        return order;
    }
}
