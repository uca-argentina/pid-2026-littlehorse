using DrinkIt.Application.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tables;

/// <summary>One table as the administration screen lists it and gets it back after a change.</summary>
public sealed record TableSummary(Guid Id, int Number, int Capacity, string? Location, bool IsActive, AuditInfo Audit)
{
    internal static TableSummary Of(Table table) =>
        new(table.Id, table.Number, table.Capacity, table.Location, table.IsActive, AuditInfo.Of(table));
}

public static class TableErrors
{
    /// <summary>
    /// Nobody in this venue has that table. A table of another venue answers the
    /// same: the global query filter hides it, and saying otherwise would let an
    /// administrator probe for ids elsewhere.
    /// </summary>
    public static readonly Error NotFound =
        new("table.not_found", "This venue has no table with that id.");

    /// <summary>
    /// The number belongs to a table of this venue, active or not. A table that
    /// was put away keeps its number, so what orders say ("Mesa 5") never ends up
    /// pointing at another table: it comes back with reactivation.
    /// </summary>
    /// <summary>
    /// US-39's edge: somebody bought the table for a night that is still to
    /// come or underway, so it cannot be put away under them.
    /// </summary>
    public static readonly Error InANightNotOver =
        new("table.in_a_night_not_over", "The table is in a night that has not ended. Take it out of the night first.");

    public static readonly Error NumberTaken =
        new("table.number_taken", "This venue already has a table with that number.");
}
