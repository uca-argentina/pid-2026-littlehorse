using DrinkIt.Application.Common;

namespace DrinkIt.Application.Kds;

/// <summary>Failures the bar's use cases answer with.</summary>
public static class KdsErrors
{
    /// <summary>
    /// No order of this venue has that code. Deliberately the same answer for
    /// a code that belongs to another venue and for one that is not a code at
    /// all: telling them apart would say which codes exist elsewhere.
    /// </summary>
    public static readonly Error OrderNotFound =
        new("kds.order_not_found", "This venue has no order with that code.");

    /// <summary>
    /// What was scanned leads to none of this venue's orders. Same reasoning as
    /// <see cref="OrderNotFound"/>: another venue's QR, a stranger's QR and
    /// something that is not a QR of ours at all read alike.
    /// </summary>
    public static readonly Error UnknownCode =
        new("kds.unknown_code", "What was scanned is not one of this venue's orders.");

    /// <summary>
    /// US-36: a real order of this venue, scanned at a bar it did not go to.
    /// Unlike the buttons, which only ever see their own bar's cards, a scan
    /// is the customer standing at the wrong bar, and they need sending on.
    /// </summary>
    public static readonly Error AtAnotherBar =
        new("kds.at_another_bar", "That order is picked up at another bar.");
}
