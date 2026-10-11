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

    /// <summary>
    /// US-37: a night starts with what the one before it left, so its stock
    /// does not exist until that one is over.
    /// </summary>
    public static readonly Error PreviousNightNotOver =
        new("night_stock.previous_night_not_over", "The stock of this night is available once the night before it is over.");

    /// <summary>
    /// The night has no stock of that product: it was never opened, or the
    /// product is not this venue's. From outside they read the same.
    /// </summary>
    public static readonly Error StockNotFound =
        new("night_stock.not_found", "This night has no stock of that product.");

    /// <summary>
    /// Sales since the screen was opened left less than the change takes away.
    /// Looking at the stock again is the fix.
    /// </summary>
    public static readonly Error StockMoved =
        new("night_stock.stock_moved", "Sales changed the stock; there is less left than that takes away.");
}
