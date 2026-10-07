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
}
