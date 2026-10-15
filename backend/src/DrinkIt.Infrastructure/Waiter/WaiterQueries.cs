using DrinkIt.Application.Common;
using DrinkIt.Application.Waiter;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Waiter;

/// <summary>Read side: no repository, no tracking, a projection straight to the DTO.</summary>
internal sealed class WaiterQueries(DrinkItDbContext context, ICurrentStaffUser waiter) : IWaiterQueries
{
    public async Task<IReadOnlyList<WaiterTableOrder>> ListAsync(CancellationToken cancellationToken)
    {
        // The venue filter scopes this on its own — nothing here asks for one.
        // Only what is ready: a drink still being made has nothing to carry, and
        // a delivered one is done. The bar's own orders have no table.
        var found = await context.Orders
            .AsNoTracking()
            .Where(order => order.TableId != null && order.Status == OrderStatus.Ready)
            .OrderBy(order => order.ReadyAt)
            .Select(order => new
            {
                order.Id,
                order.TableNumber,
                order.CustomerName,
                order.ReadyAt,
                order.WaiterId,
                order.WaiterName,
                order.TakenAt,
                Items = order.Items
                    .Select(item => new WaiterOrderItem(item.ProductName, item.Quantity, item.Note))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return found
            .Select(order => new WaiterTableOrder(
                order.Id,
                order.TableNumber!.Value,
                order.CustomerName,
                order.ReadyAt,
                order.WaiterId,
                order.WaiterName,
                order.TakenAt,
                order.Items))
            .ToList();
    }

    public async Task<string> GetMyNameAsync(CancellationToken cancellationToken)
    {
        // The account of the token, of this venue by the global filter.
        Guid? me = waiter.Id;

        return await context.StaffUsers
            .AsNoTracking()
            .Where(user => user.Id == me)
            .Select(user => user.DisplayName ?? user.Username)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
    }
}
