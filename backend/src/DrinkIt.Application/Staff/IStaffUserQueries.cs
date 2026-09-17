using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

/// <summary>What the administration listing shows about one person.</summary>
public sealed record StaffUserListItem(Guid Id, string Username, StaffRole Role, bool IsActive);

/// <summary>
/// The read side. No repository here: reads have no invariant to protect, so
/// the implementation projects straight from the DbContext to this DTO.
/// </summary>
public interface IStaffUserQueries
{
    /// <summary>
    /// Everyone in the venue of the current request, deactivated staff included:
    /// a soft-deleted user still has to be visible to be reactivated.
    /// </summary>
    Task<IReadOnlyList<StaffUserListItem>> ListAsync(CancellationToken cancellationToken);
}
