using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Application.Tables;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Nights;

/// <summary>One VIP table the night sells, with the package's budget (US-40).</summary>
public sealed record NightTableRequest(Guid TableId, decimal Budget);

public sealed record CreateNightCommand(
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<Guid> CrewIds,
    IReadOnlyList<NightTableRequest>? Tables = null);

/// <summary>
/// US-35: the administrator sets up a night of their own venue. The venue
/// comes from <see cref="ICurrentVenue"/>, which reads the token claim.
/// </summary>
/// <remarks>
/// US-37: each KDS of the new night starts with an equal share of the
/// storeroom, which the administrator changes from the night's screen.
/// </remarks>
public sealed class CreateNightHandler(
    INightRepository nights,
    IStaffUserRepository staffUsers,
    ITableRepository tables,
    IVipAccountRepository vipAccounts,
    ICurrentVenue currentVenue,
    TimeProvider clock)
{
    public async Task<Result<NightSummary>> HandleAsync(CreateNightCommand command, CancellationToken cancellationToken)
    {
        HashSet<Guid> crewIds = [.. command.CrewIds];
        IReadOnlyList<StaffUser> crew = await staffUsers.ListByIdsAsync(crewIds, cancellationToken);

        if (crew.Count != crewIds.Count) return NightErrors.CrewMemberNotFound;
        if (await nights.OverlapsAsync(command.StartsAt, command.EndsAt, excluding: null, cancellationToken)) return NightErrors.Overlaps;

        // The crew is put back in the order the administrator chose it: the
        // repository gives no order guarantee. Whatever else is wrong (no KDS,
        // no cashier, bad hours) is a broken invariant and Create throws.
        Night night = Night.Create(
            currentVenue.Id,
            command.Name,
            command.StartsAt,
            command.EndsAt,
            crewIds.Select(id => crew.First(member => member.Id == id)));

        DateTimeOffset now = clock.GetUtcNow();

        // US-40: the tables go in with the night. They are only tracked here;
        // AddAsync commits them in the same transaction, or nothing at all.
        Error? refusal = await NightTables.ApplyAsync(night, crew, command.Tables ?? [], [], tables, vipAccounts, now, cancellationToken);

        if (refusal is not null) return refusal;

        StaffUser[] bars = [.. crewIds.Select(id => crew.First(member => member.Id == id)).Where(member => member.Role == StaffRole.Kds)];

        await nights.AddAsync(night, bars, now, cancellationToken);

        return NightSummary.Of(night);
    }
}
