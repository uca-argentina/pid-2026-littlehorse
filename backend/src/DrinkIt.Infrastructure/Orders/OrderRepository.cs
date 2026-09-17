using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Orders;

internal sealed class OrderRepository(DrinkItDbContext context) : IOrderRepository
{
    /// <summary>
    /// No venue in the WHERE: the global query filter adds the one the request
    /// resolved, so one venue's retry can never find another venue's order.
    /// </summary>
    public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        context.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(
                order => EF.Property<string>(order, OrderConfiguration.IdempotencyKey) == key,
                cancellationToken);

    /// <summary>
    /// One SaveChanges for the whole use case. The stock the handler took off
    /// the shelf is tracked by this same context, so it is written in the same
    /// transaction as the order that took it — an order without its stock
    /// movement is the one outcome that must never exist.
    /// </summary>
    public async Task AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken)
    {
        context.Orders.Add(order);
        context.Entry(order).Property(OrderConfiguration.IdempotencyKey).CurrentValue = idempotencyKey;

        await context.SaveChangesAsync(cancellationToken);
    }
}
