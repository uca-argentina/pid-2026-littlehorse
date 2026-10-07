using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Nights;

public sealed record UpdateNightCommand(Guid Id, string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> CrewIds);

/// <summary>
/// US-35: the administrator corrects a night, extends it or closes it early.
/// Which of those a night still allows is the aggregate's call
/// (<see cref="Night.Update"/>), judged against the server's clock.
/// </summary>
public sealed class UpdateNightHandler(
    INightRepository nights,
    IStaffUserRepository staffUsers,
    TimeProvider clock)
{
    public async Task<Result<NightSummary>> HandleAsync(UpdateNightCommand command, CancellationToken cancellationToken)
    {
        Night? night = await nights.GetForUpdateAsync(command.Id, cancellationToken);

        if (night is null) return NightErrors.NotFound;

        HashSet<Guid> crewIds = [.. command.CrewIds];
        IReadOnlyList<StaffUser> crew = await staffUsers.ListByIdsAsync(crewIds, cancellationToken);

        if (crew.Count != crewIds.Count) return NightErrors.CrewMemberNotFound;

        night.Update(
            command.Name,
            command.StartsAt,
            command.EndsAt,
            crewIds.Select(id => crew.First(member => member.Id == id)),
            clock.GetUtcNow());

        // Checked on the hours the night ended up with, not the ones asked for:
        // closing it early can only make it shorter. Nothing is saved on a
        // clash, and the tracked change dies with the request.
        if (await nights.OverlapsAsync(night.StartsAt, night.EndsAt, night.Id, cancellationToken)) return NightErrors.Overlaps;

        await nights.SaveChangesAsync(cancellationToken);

        return NightSummary.Of(night);
    }
}
