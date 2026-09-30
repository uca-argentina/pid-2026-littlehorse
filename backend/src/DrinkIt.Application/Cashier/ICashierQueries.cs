using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Cashier;

/// <summary>One drink of an order, as the till reads it back to the customer.</summary>
public sealed record CashierOrderItem(string ProductName, int Quantity, string? Note, decimal UnitPrice);

/// <summary>
/// An order as the till shows it (US-26): what to charge and for what.
/// </summary>
/// <remarks>
/// <see cref="PaidAt"/> is there so a code that was already collected can say
/// when — the wireframe's "ya cobrado", with the time.
/// </remarks>
public sealed record CashierOrder(
    string Code,
    string CustomerName,
    OrderStatus Status,
    decimal Total,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? PaidAt,
    IReadOnlyList<CashierOrderItem> Items);

/// <summary>
/// The read side of the till. A query and not a repository, per CLAUDE.md. It
/// takes no venue: the cashier's token resolved one, and the global query
/// filter is what scopes it.
/// </summary>
public interface ICashierQueries
{
    /// <summary>Every order still waiting for cash, the one that has waited longest first.</summary>
    Task<IReadOnlyList<CashierOrder>> GetAwaitingPaymentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The order behind that code, whatever its state, or null when this venue
    /// has none: the till has to tell "no such code" from "already paid".
    /// </summary>
    Task<CashierOrder?> FindAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// The order behind the token a QR carries (the customer's phone shows it
    /// from the moment they confirm), or null when this venue has none.
    /// </summary>
    Task<CashierOrder?> FindByTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>What that cashier collected since then, the latest first: the till's own shift.</summary>
    Task<IReadOnlyList<CashierOrder>> GetCollectedByAsync(string cashier, DateTimeOffset since, CancellationToken cancellationToken);
}
