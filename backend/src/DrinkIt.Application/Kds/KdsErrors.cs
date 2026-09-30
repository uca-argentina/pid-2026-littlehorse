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
}
