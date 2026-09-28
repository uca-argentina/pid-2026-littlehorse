using DrinkIt.Application.Kds;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Kds;

internal sealed class KdsQueueQueries(DrinkItDbContext context) : IKdsQueueQueries
{
    private static readonly OrderStatus[] OnTheirWay =
    [
        OrderStatus.Queued,
        OrderStatus.InPreparation,
        OrderStatus.Ready,
    ];

    public async Task<IReadOnlyList<KdsQueueOrder>> GetQueueAsync(CancellationToken cancellationToken)
    {
        // The venue filter scopes this on its own — nothing here asks for one.
        // Read-only and projected straight to the shape the board draws.
        var found = await context.Orders
            .AsNoTracking()
            .Where(order => OnTheirWay.Contains(order.Status))
            .OrderBy(order => order.PaidAt)
            .Select(order => new
            {
                order.Code,
                order.CustomerName,
                order.Status,
                order.PaidAt,
                order.Method,
                OrderItems = order.Items
                    .Select(item => new KdsQueueOrderItem(item.ProductName, item.Quantity, item.Note))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return found
            .Select(order => new KdsQueueOrder(
                order.Code.Value,
                order.CustomerName,
                order.Status,
                order.PaidAt!.Value,
                order.Method!.Value.IsForTable(),
                order.OrderItems))
            .ToList();
    }
}
