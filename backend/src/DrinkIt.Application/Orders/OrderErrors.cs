using DrinkIt.Application.Common;

namespace DrinkIt.Application.Orders;

/// <summary>Failures of the customer's use cases on an order that more than one place answers with.</summary>
public static class OrderErrors
{
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

    /// <summary>
    /// The link leads to no order: a wrong token, a code nobody has, another
    /// venue's order. One answer for all of them, like the tracking link.
    /// </summary>
    public static readonly Error NotFound =
        new("order.not_found", "That link does not lead to an order.");

    /// <summary>
    /// Only an order waiting to be paid at the till can be canceled (US-23).
    /// Paid, by any method, it is the bar's, and paid orders are not given back.
    /// </summary>
    public static readonly Error NotCancelable =
        new("order.not_cancelable", "Only an order waiting to be paid at the till can be canceled.");
}
