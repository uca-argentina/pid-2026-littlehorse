using DrinkIt.Application.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// The write side of the order aggregate.
/// </summary>
/// <remarks>
/// The idempotency key travels beside the order rather than inside it: it is
/// how the request arrived, not something true about the drinks. The domain
/// never sees it.
/// </remarks>
public interface IOrderRepository
{
    /// <summary>
    /// The order a previous attempt with this key already created, or null when
    /// this is the first time it arrives. Scoped to the venue of the request by
    /// the global query filter, like everything else.
    /// </summary>
    Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the order under that key, and answers what the database made of
    /// it.
    /// </summary>
    /// <remarks>
    /// This is the single save of the whole use case: the stock the handler
    /// took off the shelf is written in the same unit of work as the order that
    /// took it. An order that exists without its stock movement, or the other
    /// way round, is the one outcome this must never produce.
    ///
    /// Two things can have happened underneath while the handler was working,
    /// and both are outcomes rather than bugs:
    /// <list type="bullet">
    /// <item>
    /// The stock moved, so the write was refused and nothing was saved. It
    /// comes back as <see cref="OrderErrors.StockMoved"/>, and nothing of this
    /// attempt is left pending: the caller may read the products again and try
    /// the whole thing once more.
    /// </item>
    /// <item>
    /// Another attempt with the same key got there first — a retry that landed
    /// while this one was still in flight. That order comes back as the value,
    /// so the customer sees one order and one code either way.
    /// </item>
    /// </list>
    /// </remarks>
    Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// The whole order, lines included and tracked, so a use case can move it
    /// along. Null when this venue has no order with that code — including
    /// when another venue does, since the global query filter hides it.
    /// </summary>
    Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken);

    /// <summary>
    /// The same, found by the secret the customer's QR carries instead of by
    /// the code said out loud (US-20). Another venue's order is null here too.
    /// </summary>
    Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken);

    /// <summary>
    /// The same, found by its id — what a payment gateway carries back as its
    /// external reference (US-24). Another venue's order is null here too.
    /// </summary>
    Task<Order?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// This venue's orders still waiting for a payment that were placed before
    /// <paramref name="before"/>, whole and tracked, so each can be canceled.
    /// </summary>
    Task<IReadOnlyList<Order>> GetAwaitingPaymentCreatedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken);

    /// <summary>
    /// Commits an order canceled because nobody paid for it (US-24), putting
    /// the drinks it took back on the menu in the same write: they were never
    /// sold.
    /// </summary>
    Task SaveReturningStockAsync(Order order, CancellationToken cancellationToken);

    /// <summary>
    /// Commits what the use case changed, and only then reacts to the events
    /// the order raised — so the board never hears about a change that did not
    /// make it to the database.
    /// </summary>
    /// <remarks>
    /// Refused with <see cref="OrderErrors.ChangedMeanwhile"/> when somebody
    /// else saved the same order after it was loaded here: two tills collecting
    /// it, two tablets moving it. Nothing is written and nobody is notified;
    /// the first one stands.
    /// </remarks>
    Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken);
}
