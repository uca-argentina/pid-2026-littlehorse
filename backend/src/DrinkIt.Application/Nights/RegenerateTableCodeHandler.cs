using DrinkIt.Application.Common;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.Nights;

/// <summary>The code of a table in a night, as the night's screen shows it after a change.</summary>
public sealed record NightTableCode(Guid TableId, string AccessCode);

/// <summary>
/// US-40, criterion 4: the code of a table leaked, so it gets a new one. The
/// old one stops working; the budget and what was spent stay.
/// </summary>
public sealed class RegenerateTableCodeHandler(
    INightRepository nights,
    IVipAccountRepository vipAccounts,
    TimeProvider clock)
{
    public async Task<Result<NightTableCode>> HandleAsync(Guid nightId, Guid tableId, CancellationToken cancellationToken)
    {
        Night? night = await nights.GetForUpdateAsync(nightId, cancellationToken);

        if (night is null) return NightErrors.NotFound;

        IReadOnlyList<VipAccount> accounts = await vipAccounts.ListForNightAsync(night.Id, cancellationToken);
        VipAccount? account = accounts.FirstOrDefault(candidate => candidate.TableId == tableId);

        if (account is null) return NightErrors.TableNotInNight;

        account.RegenerateAccessCode(night, clock.GetUtcNow());

        if (!await nights.SaveChangesAsync(cancellationToken)) return NightErrors.TablesMoved;

        return new NightTableCode(account.TableId, account.AccessCode);
    }
}
