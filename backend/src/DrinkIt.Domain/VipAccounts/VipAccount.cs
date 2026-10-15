using System.Security.Cryptography;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Domain.VipAccounts;

/// <summary>
/// A VIP table in one night (US-40): the package sold for it, as a budget the
/// table's orders spend. The table is the venue's and stays; the account lives
/// one night and closes when the night ends, so there is no "close" to call.
/// </summary>
/// <remarks>
/// Its own aggregate, not part of <see cref="Night"/>: the balance is money two
/// phones of the same table can spend at once (US-41), and it is guarded by a
/// concurrency token of its own.
/// </remarks>
public sealed class VipAccount : AuditStamps, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string TableFromAnotherVenue = "vip_account.table_from_another_venue";
        public const string TableInactive = "vip_account.table_inactive";
        public const string BudgetOutOfRange = "vip_account.budget_out_of_range";
        public const string NightOver = "vip_account.night_over";
        public const string HasSpending = "vip_account.has_spending";
    }

    /// <summary>What fits the money columns: ten digits, two of them cents.</summary>
    public const decimal BudgetMaxValue = 99_999_999.99m;

    /// <summary>Hex characters of the access code: 128 random bits, as the tracking token.</summary>
    public const int AccessCodeLength = 32;

    private VipAccount(Guid id, Guid venueId, Guid nightId, Guid tableId, decimal budget, string accessCode)
    {
        Id = id;
        VenueId = venueId;
        NightId = nightId;
        TableId = tableId;
        Budget = budget;
        AccessCode = accessCode;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public Guid NightId { get; }

    public Guid TableId { get; }

    /// <summary>The package sold for the night: everything loaded onto the table.</summary>
    public decimal Budget { get; private set; }

    /// <summary>What the table's orders took from it. Kept apart so changing the budget never undoes a sale.</summary>
    public decimal Spent { get; private set; }

    public decimal Balance => Budget - Spent;

    /// <summary>
    /// The secret in the QR the waiter hands over (§8.2): it identifies the
    /// table and its night, nobody's identity.
    /// </summary>
    public string AccessCode { get; private set; }

    public static VipAccount Open(Night night, Table table, decimal budget, DateTimeOffset now)
    {
        if (table.VenueId != night.VenueId) throw new DomainException(ErrorCodes.TableFromAnotherVenue, "The table belongs to another venue.");
        if (!table.IsActive) throw new DomainException(ErrorCodes.TableInactive, "The table was put away: bring it back before giving it a night.");

        CheckOpen(night, now);
        CheckBudget(budget);

        return new VipAccount(Guid.CreateVersion7(), night.VenueId, night.Id, table.Id, budget, NewAccessCode())
        {
            CreatedAt = now,
        };
    }

    /// <summary>
    /// What the package is worth now. Spent stays as it is, so a sale made
    /// meanwhile is never undone; the balance follows from both.
    /// </summary>
    public void ChangeBudget(Night night, decimal budget, DateTimeOffset now)
    {
        CheckSameNight(night);
        CheckOpen(night, now);
        CheckBudget(budget);

        Budget = budget;
    }

    /// <summary>
    /// Throws when the table cannot be taken out of the night; says nothing
    /// otherwise. A table that already spent keeps its account: its orders
    /// point at it, and the money was taken.
    /// </summary>
    public void EnsureCanLeave(Night night, DateTimeOffset now)
    {
        CheckSameNight(night);
        CheckOpen(night, now);

        if (Spent > 0) throw new DomainException(ErrorCodes.HasSpending, "The table already spent part of its budget.");
    }

    private static void CheckOpen(Night night, DateTimeOffset now)
    {
        if (night.HasEndedAt(now)) throw new DomainException(ErrorCodes.NightOver, "The night is over: its tables are only looked at.");
    }

    private static void CheckBudget(decimal budget)
    {
        if (budget is <= 0 or > BudgetMaxValue) throw new DomainException(ErrorCodes.BudgetOutOfRange, $"The budget has to be more than 0 and at most {BudgetMaxValue}.");
    }

    // A programming error, not a rule: the caller loaded the wrong night.
    private void CheckSameNight(Night night) =>
        ArgumentOutOfRangeException.ThrowIfNotEqual(night.Id, NightId, nameof(night));

    private static string NewAccessCode() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(AccessCodeLength / 2));
}
