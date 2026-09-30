using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DrinkIt.Infrastructure.Orders;

internal sealed partial class OrderRepository(
    DrinkItDbContext context,
    IDomainEventDispatcher events,
    ILogger<OrderRepository> logger) : IOrderRepository
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

            // Not the request's token: the order is committed, and a customer
            // who closes the app right after paying must not cancel the
            // notification that puts their drink in front of the bar.
            await DispatchWithoutFailingTheOrder(order, CancellationToken.None);

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

    /// <summary>
    /// No venue in the WHERE, same as above: the code is unique per venue, and
    /// the global query filter is what makes it this venue's.
    /// </summary>
    public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
        context.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Code == code, cancellationToken);

    /// <summary>
    /// An equality in SQL rather than <see cref="TrackingToken.Matches"/>: the
    /// constant-time comparison guards a guess against one known order, and
    /// here there is no order yet — only an index to look the token up in.
    /// </summary>
    public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
        context.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.TrackingToken == token, cancellationToken);

    /// <summary>By the id the payment gateway carries back. Filtered by venue like the rest.</summary>
    public Task<Order?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        context.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <summary>
    /// The creation stamp is when it started waiting: a digital order is born
    /// waiting for its payment (US-24).
    /// </summary>
    public async Task<IReadOnlyList<Order>> GetAwaitingPaymentCreatedBeforeAsync(
        DateTimeOffset before,
        CancellationToken cancellationToken) =>
        await context.Orders
            .Include(order => order.Items)
            .Where(order => order.Status == OrderStatus.AwaitingPayment && order.CreatedAt < before)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The cancel and the drinks going back, in one transaction, the mirror of
    /// <see cref="AddAsync"/>: an order canceled while its stock stayed taken,
    /// or stock returned for an order still waiting, must never be left behind.
    /// </summary>
    public async Task SaveReturningStockAsync(Order order, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        foreach (OrderItem item in order.Items)
        {
            int quantity = item.Quantity;

            await context.Products
                .Where(product => product.Id == item.ProductId)
                .ExecuteUpdateAsync(
                    row => row.SetProperty(product => product.Stock, product => product.Stock + quantity),
                    cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await DispatchWithoutFailingTheOrder(order, CancellationToken.None);
    }

    public async Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody else saved this order after it was loaded here (see
            // Version in OrderConfiguration). Nothing was written, so nobody
            // hears about it: the events are forgotten with the change.
            order.ClearDomainEvents();
            context.ChangeTracker.Clear();
            LogChangedMeanwhile(logger, order.Code.Value);

            return OrderErrors.ChangedMeanwhile;
        }

        // Committed by now, so the same reasoning as a new order applies.
        await DispatchWithoutFailingTheOrder(order, CancellationToken.None);

        return order;
    }

    /// <summary>
    /// The order is already committed by the time this runs, so a notifier
    /// that is down is not a reason to tell the customer their money did not
    /// go through: it did, and the drinks are queued either way. Losing the
    /// order over a dropped notification would be strictly worse.
    /// </summary>
    /// <remarks>
    /// The board does not catch up on its own yet: it only reloads on the next
    /// notification or when its connection comes back. That is US-31. Until
    /// then the log line is the only trace of an order the bar never saw.
    /// </remarks>
    private async Task DispatchWithoutFailingTheOrder(Order order, CancellationToken cancellationToken)
    {
        try
        {
            await events.DispatchAsync(order.DomainEvents, cancellationToken);
        }
        catch (Exception exception)
        {
            // Not rethrown — see the remarks above. Nothing here is still in a
            // position to undo the write that already succeeded.
            LogBoardNotNotified(logger, order.Code.Value, exception);
        }
        finally
        {
            order.ClearDomainEvents();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Order {OrderCode} was changed by somebody else while this change was on its way; nothing was saved")]
    private static partial void LogChangedMeanwhile(ILogger logger, string orderCode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Order {OrderCode} was saved, but the KDS board could not be notified")]
    private static partial void LogBoardNotNotified(ILogger logger, string orderCode, Exception exception);
}
