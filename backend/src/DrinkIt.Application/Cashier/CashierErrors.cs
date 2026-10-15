using DrinkIt.Application.Common;

namespace DrinkIt.Application.Cashier;

/// <summary>Failures the till's use cases answer with.</summary>
public static class CashierErrors
{
    /// <summary>
    /// No order of this venue has that code. The same answer for another
    /// venue's code and for something that is not a code at all.
    /// </summary>
    public static readonly Error OrderNotFound =
        new("cashier.order_not_found", "This venue has no order with that code.");

    /// <summary>
    /// Nothing left to collect: it was paid already, at the till or from the
    /// phone. A second tap, or the customer showing the code twice.
    /// </summary>
    public static readonly Error AlreadyPaid =
        new("cashier.already_paid", "That order is not waiting to be paid.");

    /// <summary>
    /// US-36: cash takes its drinks when it is collected, and by then no bar
    /// of the night has all of them. Asked before the money changes hands.
    /// The subject names the drink, so the till can say which.
    /// </summary>
    public static readonly Error SoldOut =
        new("cashier.sold_out", "One of the drinks ran out at every bar.");

    /// <summary>
    /// The night the order was placed in is over: its bars and their stock are
    /// done with. Nothing is charged; the customer orders again tonight.
    /// </summary>
    public static readonly Error NightOver =
        new("cashier.night_over", "That order belongs to a night that is over.");
}
