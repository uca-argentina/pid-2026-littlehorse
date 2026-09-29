using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>US-18: "Volver a preparación" — marked ready by mistake.</summary>
public sealed class ReturnToPreparationHandler(IOrderRepository orders)
{
    public Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken) =>
        KdsOrderMove.ApplyAsync(orders, code, order => order.ReturnToPreparation(), cancellationToken);
}
