using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>US-18: "Deshacer", right after a mistaken delivery.</summary>
public sealed class UndoDeliveryHandler(IOrderRepository orders, TimeProvider clock)
{
    public Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken) =>
        KdsOrderMove.ApplyAsync(orders, code, order => order.UndoDelivery(clock.GetUtcNow()), cancellationToken);
}
