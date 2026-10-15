using DrinkIt.Application.Common;

namespace DrinkIt.Application.Waiter;

/// <summary>Failures of the waiter's use cases (US-43).</summary>
public static class WaiterErrors
{
    /// <summary>
    /// No table's order of this venue has that id. An order picked up at the
    /// bar, another venue's and one that never existed answer the same: a
    /// waiter has no business telling them apart.
    /// </summary>
    public static readonly Error OrderNotFound =
        new("waiter.order_not_found", "There is no table order with that id.");

    /// <summary>
    /// Another waiter tapped first (criterion 3). Nothing was written; the
    /// screen reloads and the order is under that waiter's name.
    /// </summary>
    public static readonly Error AlreadyTaken =
        new("waiter.already_taken", "Another waiter is already carrying that order.");
}
