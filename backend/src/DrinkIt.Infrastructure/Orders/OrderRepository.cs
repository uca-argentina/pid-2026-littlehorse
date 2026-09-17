using DrinkIt.Application.Common;
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
    public async Task<Result<Order>> AddAsync(
        Order order,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        context.Orders.Add(order);
        context.Entry(order).Property(OrderConfiguration.IdempotencyKey).CurrentValue = idempotencyKey;

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return order;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Stock is a concurrency token, so the UPDATE carried the value it
            // was read at and matched no row: somebody else sold from the same
            // product in between. Nothing was written, order included.
            //
            // The tracker is emptied so the caller can simply start again: what
            // it holds is an order that does not exist and stock counts that
            // are out of date, and reading the products again has to reach the
            // database rather than these.
            context.ChangeTracker.Clear();

            return OrderErrors.StockMoved;
        }
        catch (DbUpdateException)
        {
            // A unique index refused the insert. If it was the idempotency one,
            // a retry of this very order got there first while this attempt was
            // in flight, and that order is the answer to both of them.
            context.ChangeTracker.Clear();

            Order? alreadyWritten = await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

            // Any other index means something we have not thought about, and
            // swallowing it would hide it.
            if (alreadyWritten is null) throw;

            return alreadyWritten;
        }
    }
}
