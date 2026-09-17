using DrinkIt.Application.Common;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

public sealed record ChangeStaffUserRoleCommand(Guid StaffUserId, StaffRole Role);

/// <summary>
/// Corrects a role that was assigned wrong. The person keeps their account, so
/// what they did under the old role still points at them and they sign in with
/// the password they always had.
/// </summary>
public sealed class ChangeStaffUserRoleHandler(IStaffUserRepository staffUsers)
{
    public async Task<Result<StaffUserSummary>> HandleAsync(
        ChangeStaffUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        StaffUser? user = await staffUsers.GetForUpdateAsync(command.StaffUserId, cancellationToken);

        if (user is null) return StaffUserErrors.NotFound;

        if (await WouldLeaveTheVenueWithoutAnAdministrator(user, command.Role, cancellationToken))
            return StaffUserErrors.LastAdministrator;

        // A role the venue does not hand out is a broken invariant, and
        // ChangeRole throws. The API turns that into a 400; restating the rule
        // here would be a second copy that drifts.
        user.ChangeRole(command.Role);

        await staffUsers.SaveChangesAsync(cancellationToken);

        return new StaffUserSummary(user.Id, user.Username, user.Role, user.IsActive);
    }

    /// <summary>
    /// Demoting the last administrator locks the venue out exactly like
    /// deactivating them. Saving the role they already have is not a demotion,
    /// and somebody already deactivated takes nothing away.
    /// </summary>
    private async Task<bool> WouldLeaveTheVenueWithoutAnAdministrator(
        StaffUser user,
        StaffRole wanted,
        CancellationToken cancellationToken)
    {
        if (wanted == StaffRole.Administrator) return false;
        if (user is not { IsActive: true, Role: StaffRole.Administrator }) return false;

        return await staffUsers.CountActiveAdministratorsAsync(cancellationToken) <= 1;
    }
}
