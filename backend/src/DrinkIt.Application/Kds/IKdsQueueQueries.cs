using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>One drink on a queued order, as the bar's tablet draws it.</summary>
public sealed record KdsQueueOrderItem(string ProductName, int Quantity, string? Note);

/// <summary>
/// One order on the KDS board (US-15): everything the bar needs to decide what
/// to make next, without opening anything else.
/// </summary>
/// <remarks>
/// <see cref="IsForTable"/> is not a stored delivery type — <see cref="Order"/>
/// has none. It is read straight off the payment method: a VIP balance means a
/// table, cash and digital both mean the bar, whichever moment the money
/// actually changed hands (<see cref="PaymentMethods.IsForTable"/>).
/// </remarks>
public sealed record KdsQueueOrder(
    string Code,
    string CustomerName,
    OrderStatus Status,
    DateTimeOffset PaidAt,
    DateTimeOffset? LastModifiedAt,
    bool IsForTable,
    IReadOnlyList<KdsQueueOrderItem> OrderItems);

/// <summary>
/// The bar's queue: every order still on its way to this bar, oldest paid
/// first (US-15). Another bar's orders are not on it (US-36).
/// </summary>
/// <remarks>
/// A query and not a repository, per CLAUDE.md: nothing here changes anything,
/// so it goes straight to EF Core with a projection. It takes no venue and no
/// bar — the station's own token resolved both before this ran; a parameter
/// here would be a second way to ask for somebody else's queue that the token
/// did not close off.
/// </remarks>
public interface IKdsQueueQueries
{
    Task<IReadOnlyList<KdsQueueOrder>> GetQueueAsync(CancellationToken cancellationToken);
}
