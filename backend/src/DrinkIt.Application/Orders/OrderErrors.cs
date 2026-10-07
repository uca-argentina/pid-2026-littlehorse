using DrinkIt.Application.Common;

namespace DrinkIt.Application.Orders;

/// <summary>Failures of confirming an order that more than one place answers with.</summary>
public static class OrderErrors
{
    /// <summary>
    /// No night of the venue is on (US-35, criterion 3). The menu is still
    /// readable; what is closed is confirming. Also the answer for a cart put
    /// together before closing and confirmed after it.
    /// </summary>
    public static readonly Error NotTakingOrders =
        new("order.not_taking_orders", "The venue is not taking orders right now.");

    /// <summary>
    /// Somebody else's order changed the stock between it being read and this
    /// one being written. Nothing was saved. The customer is told to look at
    /// their order again rather than told a drink ran out, because it may not
    /// have: the stock moved, and what it moved to is now anybody's guess.
    /// </summary>
    public static readonly Error StockMoved =
        new("order.stock_moved", "Somebody ordered at the same time and the stock changed. Check your order.");

    /// <summary>
    /// Somebody else changed this order between it being loaded and saved
    /// here — another till, another tablet. Nothing was saved: theirs stands.
    /// </summary>
    public static readonly Error ChangedMeanwhile =
        new("order.changed_meanwhile", "Somebody else changed this order at the same time. Look at it again.");
}
