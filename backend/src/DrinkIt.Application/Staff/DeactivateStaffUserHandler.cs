using DrinkIt.Application.Common;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

/// <summary>
/// Takes access away without taking the row away. The orders that person
/// prepared keep pointing at their account, which is the whole reason this is a
/// soft delete and not a DELETE.
/// </summary>
public sealed class DeactivateStaffUserHandler(IStaffUserRepository staffUsers)
{
    public async Task<Result<StaffUserSummary>> HandleAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        StaffUser? user = await staffUsers.GetForUpdateAsync(staffUserId, cancellationToken);

        if (user is null) return StaffUserErrors.NotFound;

        if (await WouldLeaveTheVenueWithoutAnAdministrator(user, cancellationToken))
            return StaffUserErrors.LastAdministrator;

        user.Deactivate();

        await staffUsers.SaveChangesAsync(cancellationToken);

        return StaffUserSummary.Of(user);
    }

    /// <summary>
    /// Only an active administrator can be the last one, and only then is the
    /// count worth a query. Someone already deactivated takes nothing away.
    /// </summary>
    private async Task<bool> WouldLeaveTheVenueWithoutAnAdministrator(
        StaffUser user,
        CancellationToken cancellationToken)
    {
        if (user is not { IsActive: true, Role: StaffRole.Administrator }) return false;

        return await staffUsers.CountActiveAdministratorsAsync(cancellationToken) <= 1;
    }
}
