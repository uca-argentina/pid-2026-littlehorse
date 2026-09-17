using DrinkIt.Application.Common;

namespace DrinkIt.Application.Orders;

/// <summary>Failures of confirming an order that more than one place answers with.</summary>
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
}
