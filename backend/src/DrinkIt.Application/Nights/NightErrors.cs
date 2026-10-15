using DrinkIt.Application.Common;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Nights;

/// <summary>What the administration shows about one night.</summary>
public sealed record NightSummary(
    Guid Id,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<Guid> CrewIds,
    AuditInfo Audit)
{
    internal static NightSummary Of(Night night) =>
        new(night.Id, night.Name, night.StartsAt, night.EndsAt, night.CrewIds, AuditInfo.Of(night));
}

public static class NightErrors
{
    /// <summary>US-40: this venue has no table with one of the ids. Another venue's reads the same.</summary>
    public static readonly Error TableNotFound =
        new("night.table_not_found", "One of the chosen tables is not part of this venue.");

    /// <summary>US-40: a table is in a night once, with one budget.</summary>
    public static readonly Error TableRepeated =
        new("night.table_repeated", "The same table was chosen twice.");

    /// <summary>This venue has no such night. Another venue's reads the same, as with <c>staff.not_found</c>.</summary>
    public static readonly Error NotFound =
        new("night.not_found", "This venue has no night with that id.");

    /// <summary>
    /// US-35, criterion 1: an order confirmed in a shared minute would not know
    /// which night it belongs to.
    /// </summary>
    public static readonly Error Overlaps =
        new("night.overlaps", "Those hours overlap another night of this venue.");

    /// <summary>
    /// Nobody in this venue has one of the ids. Whether it exists in another
    /// venue is deliberately indistinguishable, as with <c>staff.not_found</c>.
    /// </summary>
    public static readonly Error CrewMemberNotFound =
        new("night.crew_member_not_found", "One of the chosen accounts is not part of this venue.");

    /// <summary>US-37, criterion 3: a night that ended keeps the stock it had, to be looked at.</summary>
    public static readonly Error StockClosed =
        new("night.stock_closed", "The night is over: its stock can only be looked at.");

    /// <summary>
    /// US-37: loading a bar takes from the storeroom, the general stock minus
    /// what the bars of nights not yet over hold, and there is less than that.
    /// </summary>
    public static readonly Error StoreroomShort =
        new("night.storeroom_short", "There are not that many left in the storeroom to load.");

    /// <summary>
    /// US-37: a KDS's stock that night is only sold, shown and given back
    /// while it works the night, so it cannot leave it holding any.
    /// </summary>
    public static readonly Error BarHoldsStock =
        new("night.bar_holds_stock", "A KDS still holds stock in this night. Give it back to the storeroom before taking it out.");

    /// <summary>US-37: no KDS of this venue has that id. Another venue's reads the same.</summary>
    public static readonly Error BarNotFound =
        new("night.bar_not_found", "This venue has no bar with that id.");

    /// <summary>US-37: the bar was taken out of the night's crew while its stock was open.</summary>
    public static readonly Error BarNotInNight =
        new("night.bar_not_in_night", "That bar does not work this night any more.");
}
