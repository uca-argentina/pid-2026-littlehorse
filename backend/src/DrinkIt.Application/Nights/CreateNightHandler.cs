using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Nights;

public sealed record CreateNightCommand(string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> CrewIds);

/// <summary>
/// US-35: the administrator sets up a night of their own venue. The venue
/// comes from <see cref="ICurrentVenue"/>, which reads the token claim.
/// </summary>
public sealed class CreateNightHandler(
    INightRepository nights,
    IStaffUserRepository staffUsers,
    ICurrentVenue currentVenue)
{
    public async Task<Result<NightSummary>> HandleAsync(CreateNightCommand command, CancellationToken cancellationToken)
    {
        HashSet<Guid> crewIds = [.. command.CrewIds];
        IReadOnlyList<StaffUser> crew = await staffUsers.ListByIdsAsync(crewIds, cancellationToken);

        if (crew.Count != crewIds.Count) return NightErrors.CrewMemberNotFound;
        if (await nights.OverlapsAsync(command.StartsAt, command.EndsAt, cancellationToken)) return NightErrors.Overlaps;

        // The crew is put back in the order the administrator chose it: the
        // repository gives no order guarantee. Whatever else is wrong (no KDS,
        // no cashier, bad hours) is a broken invariant and Create throws.
        Night night = Night.Create(
            currentVenue.Id,
            command.Name,
            command.StartsAt,
            command.EndsAt,
            crewIds.Select(id => crew.First(member => member.Id == id)));

        await nights.AddAsync(night, cancellationToken);

        return NightSummary.Of(night);
    }
}
