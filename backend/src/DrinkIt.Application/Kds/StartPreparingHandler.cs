using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>
/// US-16: the bar takes an order off Nuevos — "Imprimir" on the board. Several
/// taken together are several calls to this, one per order, so each one is
/// taken on its own and one that fails leaves the rest taken.
/// </summary>
public sealed class StartPreparingHandler(IOrderRepository orders)
{
    public async Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return KdsErrors.OrderNotFound;

        Order? order = await orders.GetForUpdateAsync(parsed!, cancellationToken);

        if (order is null) return KdsErrors.OrderNotFound;

        order.StartPreparing();

        await orders.SaveAsync(order, cancellationToken);

        return order.Status;
    }
}
