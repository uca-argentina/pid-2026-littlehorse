using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    /// The order and the stock it sold, in one transaction. An order that
    /// exists without its stock movement, or the other way round, is the one
    /// outcome this must never produce.
    /// </summary>
    /// <remarks>
    /// The stock comes down with one conditional statement per drink — "take
    /// two off, but only if there are two" — rather than by writing back a
    /// number that was read seconds ago. That is what settles two customers
    /// reaching for the last one: exactly one of the two statements matches a
    /// row. It is also why Stock is not a concurrency token; making it one put
    /// it in the WHERE of every edit of a product, and uploading a photo while
    /// the bar sold that drink failed with a 500.
    /// </remarks>
    public async Task<Result<Order>> AddAsync(
        Order order,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        foreach (OrderItem item in order.Items)
        {
            int quantity = item.Quantity;

            // The venue filter applies to this too, so no order can take stock
            // off a product that is not its venue's.
            int sold = await context.Products
                .Where(product => product.Id == item.ProductId && product.Stock >= quantity)
                .ExecuteUpdateAsync(
                    row => row.SetProperty(product => product.Stock, product => product.Stock - quantity),
                    cancellationToken);

            if (sold == 1) continue;

            // Somebody else got there in the seconds this order took to price.
            // Nothing is written, and the caller may try the whole thing again
            // against the menu as it is now.
            await transaction.RollbackAsync(cancellationToken);

            return OrderErrors.StockMoved;
        }

        context.Orders.Add(order);
        context.Entry(order).Property(OrderConfiguration.IdempotencyKey).CurrentValue = idempotencyKey;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return order;
        }
        catch (DbUpdateException)
        {
            // A unique index refused the insert. If it was the idempotency one,
            // a retry of this very order got there first while this attempt was
            // in flight, and that order is the answer to both of them.
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();

            Order? alreadyWritten = await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

            // Any other index means something we have not thought about, and
            // swallowing it would hide it.
            if (alreadyWritten is null) throw;

            return alreadyWritten;
        }
    }
}
