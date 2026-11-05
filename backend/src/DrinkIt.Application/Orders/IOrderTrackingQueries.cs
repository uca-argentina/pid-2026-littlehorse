using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>One drink of an order, as the customer watching it sees it.</summary>
/// <remarks><paramref name="IsReward"/> is the free drink of a stamp card (US-59): the screen says "Regalo" instead of a price.</remarks>
public sealed record TrackedOrderItem(string ProductName, int Quantity, string? Note, bool IsReward = false);

/// <summary>
/// An order as its tracking screen draws it.
/// </summary>
/// <remarks>
/// The tracking token is deliberately not here. This is asked for every three
/// seconds and its answer travels to a phone, is kept by caches and shows up in
/// logs; the secret that guards the order has no business in any of that. The
/// screen already holds it — it is in the address it used to ask.
/// </remarks>
public sealed record TrackedOrder(
    string Code,
    string CustomerName,
    OrderStatus Status,
    decimal Total,
    DateTimeOffset? PaidAt,
    string? BarName,
    IReadOnlyList<TrackedOrderItem> Items,
    int? TableNumber = null,
    string? WaiterName = null,
    decimal Savings = 0);

/// <summary>
/// The read side of an order, for the customer who placed it.
/// </summary>
/// <remarks>
/// A query and not a repository: nothing here changes anything, so it goes
/// straight to EF Core with a projection, as CLAUDE.md asks.
/// </remarks>
public interface IOrderTrackingQueries
{
    /// <summary>
    /// The order that link leads to, or null when it leads nowhere.
    /// </summary>
    /// <remarks>
    /// Null covers every way of not getting in, and on purpose: a wrong token,
    /// a code nobody has, another venue's order, and an order that is already
    /// over all answer the same. Telling them apart would confirm to somebody
    /// working through codes which ones exist.
    /// </remarks>
    Task<TrackedOrder?> FindAsync(string code, string? token, CancellationToken cancellationToken);
}
