using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>US-18: "Entregado" by hand, for when the order cannot be scanned.</summary>
public sealed class DeliverHandler(IOrderRepository orders, TimeProvider clock)
{
    public Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken) =>
        KdsOrderMove.ApplyAsync(orders, code, order => order.Deliver(clock.GetUtcNow()), cancellationToken);
}
