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
}
