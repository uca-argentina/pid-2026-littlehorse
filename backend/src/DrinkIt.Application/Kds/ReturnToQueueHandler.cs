using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>US-16, criterion 4: an order taken by mistake goes back to Nuevos, as old as it was.</summary>
public sealed class ReturnToQueueHandler(IOrderRepository orders)
{
    public async Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return KdsErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        if (order is null) return KdsErrors.OrderNotFound;

        order.ReturnToQueue();

        await orders.SaveAsync(order, cancellationToken);

        return order.Status;
    }
}
