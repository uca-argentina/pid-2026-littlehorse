using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.Nights;

/// <summary>
/// US-40: the VIP tables a night sells, as its form sends them — the whole
/// list it ends up with. Shared by creating a night and editing one, so the
/// two can never apply different rules.
/// </summary>
internal static class NightTables
{
    /// <summary>
    /// Opens the accounts that are new, changes the budgets that moved and
    /// takes out the tables left out. Only tracks the changes: the caller's
    /// save commits them with the night. Returns the failure an administrator
    /// can act on, or null; broken invariants throw from the domain.
    /// </summary>
    public static async Task<Error?> ApplyAsync(
        Night night,
        IEnumerable<StaffUser> crew,
        IReadOnlyList<NightTableRequest> wanted,
        IReadOnlyList<VipAccount> current,
        ITableRepository tables,
        IVipAccountRepository vipAccounts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> ids = [.. wanted.Select(table => table.TableId)];

        if (ids.Count != wanted.Count) return NightErrors.TableRepeated;

        IReadOnlyList<Table> found = await tables.ListByIdsAsync(ids, cancellationToken);

        if (found.Count != ids.Count) return NightErrors.TableNotFound;

        // On the crew the night ends up with: adding tables and taking out the
        // last waiter are the same mistake.
        if (wanted.Count > 0) Night.EnsureCrewCanServeTables(crew);

        foreach (VipAccount leaving in current.Where(account => !ids.Contains(account.TableId)))
        {
            leaving.EnsureCanLeave(night, now);
            vipAccounts.Remove(leaving);
        }

        foreach (NightTableRequest table in wanted)
        {
            VipAccount? account = current.FirstOrDefault(candidate => candidate.TableId == table.TableId);

            if (account is null) vipAccounts.Add(VipAccount.Open(night, found.First(candidate => candidate.Id == table.TableId), table.Budget, now));
            else if (account.Budget != table.Budget) account.ChangeBudget(night, table.Budget, now);
        }

        return null;
    }
}
