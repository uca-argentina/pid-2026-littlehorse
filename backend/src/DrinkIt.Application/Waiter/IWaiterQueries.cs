namespace DrinkIt.Application.Waiter;

/// <summary>One drink on a table's order, as the waiter's phone draws it.</summary>
public sealed record WaiterOrderItem(string ProductName, int Quantity, string? Note);

/// <summary>
/// One table's order that is ready to be carried (US-43). <see cref="WaiterId"/>
/// and <see cref="WaiterName"/> are null while nobody has taken it.
/// </summary>
public sealed record WaiterTableOrder(
    Guid OrderId,
    int TableNumber,
    string CustomerName,
    DateTimeOffset? ReadyAt,
    Guid? WaiterId,
    string? WaiterName,
    DateTimeOffset? TakenAt,
    IReadOnlyList<WaiterOrderItem> Items);

/// <summary>
/// The waiters' list: the venue's table orders that are ready and not yet
/// delivered, the one that has waited longest first (criterion 1).
/// </summary>
/// <remarks>
/// A query and not a repository, per CLAUDE.md. It takes no venue: the token
/// resolved it, and a parameter would be a second way to ask for another
/// venue's list that the token did not close off.
/// </remarks>
public interface IWaiterQueries
{
    Task<IReadOnlyList<WaiterTableOrder>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// How the signed-in waiter is called: their display name, or their username
    /// when they have none. What the others and the customer read when they take one.
    /// </summary>
    Task<string> GetMyNameAsync(CancellationToken cancellationToken);
}
