using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Application.Tables;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.Nights;

/// <summary>
/// Tables is the whole list the night ends up with: one left out is taken out
/// of the night. Null leaves the night's tables as they are.
/// </summary>
public sealed record UpdateNightCommand(
    Guid Id,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<Guid> CrewIds,
    IReadOnlyList<NightTableRequest>? Tables = null);

/// <summary>
/// US-35: the administrator corrects a night, extends it or closes it early.
/// Which of those a night still allows is the aggregate's call
/// (<see cref="Night.Update"/>), judged against the server's clock.
/// </summary>
public sealed class UpdateNightHandler(
    INightRepository nights,
    IStaffUserRepository staffUsers,
    ITableRepository tables,
    IVipAccountRepository vipAccounts,
    TimeProvider clock)
{
    public async Task<Result<NightSummary>> HandleAsync(UpdateNightCommand command, CancellationToken cancellationToken)
    {
        Night? night = await nights.GetForUpdateAsync(command.Id, cancellationToken);

        if (night is null) return NightErrors.NotFound;

        HashSet<Guid> crewIds = [.. command.CrewIds];
        IReadOnlyList<StaffUser> crew = await staffUsers.ListByIdsAsync(crewIds, cancellationToken);

        if (crew.Count != crewIds.Count) return NightErrors.CrewMemberNotFound;

        // US-37: a KDS taken out with stock loaded would leave it stuck, with
        // nobody to sell it and no screen to give it back from.
        IReadOnlyCollection<Guid> holding = await nights.BarsHoldingStockAsync(night.Id, cancellationToken);

        if (night.CrewIds.Any(id => !crewIds.Contains(id) && holding.Contains(id))) return NightErrors.BarHoldsStock;

        DateTimeOffset now = clock.GetUtcNow();

        night.Update(
            command.Name,
            command.StartsAt,
            command.EndsAt,
            crewIds.Select(id => crew.First(member => member.Id == id)),
            now);

        // US-40: the form sends the whole list of tables, so one left out is
        // taken out of the night. No list at all is a client that does not
        // know about tables: it keeps them as they are, and they still need a
        // waiter. Tracked here, committed by the save below.
        IReadOnlyList<VipAccount> current = await vipAccounts.ListForNightAsync(night.Id, cancellationToken);
        IReadOnlyList<NightTableRequest> wanted = command.Tables ?? [.. current.Select(account => new NightTableRequest(account.TableId, account.Budget))];
        Error? refusal = await NightTables.ApplyAsync(night, crew, wanted, current, tables, vipAccounts, now, cancellationToken);

        if (refusal is not null) return refusal;

        // Checked on the hours the night ended up with, not the ones asked for:
        // closing it early can only make it shorter. Nothing is saved on a
        // clash, and the tracked change dies with the request.
        if (await nights.OverlapsAsync(night.StartsAt, night.EndsAt, night.Id, cancellationToken)) return NightErrors.Overlaps;

        await nights.SaveChangesAsync(cancellationToken);

        return NightSummary.Of(night);
    }
}
