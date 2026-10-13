using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>US-18: "Listo" on a card in preparation — the drink is made.</summary>
public sealed class MarkReadyHandler(IOrderRepository orders, TimeProvider clock, ICurrentStaffUser kds)
{
    public Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken) =>
        KdsOrderMove.ApplyAsync(orders, kds, code, order => order.MarkReady(clock.GetUtcNow()), cancellationToken);
}
