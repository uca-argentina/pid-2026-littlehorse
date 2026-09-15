using DrinkIt.Application.Common;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

/// <summary>
/// Gives somebody who came back the account they always had: same username,
/// same password, same role. Loading them again would leave two rows and split
/// their history in half.
/// </summary>
public sealed class ReactivateStaffUserHandler(IStaffUserRepository staffUsers)
{
    public async Task<Result<StaffUserSummary>> HandleAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        StaffUser? user = await staffUsers.GetForUpdateAsync(staffUserId, cancellationToken);

        if (user is null) return StaffUserErrors.NotFound;

        user.Activate();

        await staffUsers.SaveChangesAsync(cancellationToken);

        return new StaffUserSummary(user.Id, user.Username, user.Role, user.IsActive);
    }
}
