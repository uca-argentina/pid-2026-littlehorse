using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>US-16, criterion 4: an order taken by mistake goes back to Nuevos, as old as it was.</summary>
public sealed class ReturnToQueueHandler(IOrderRepository orders)
{
    public Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken) =>
        KdsOrderMove.ApplyAsync(orders, code, order => order.ReturnToQueue(), cancellationToken);
}
