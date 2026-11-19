using DrinkIt.Application.Common;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;


public sealed class MarkStaffAvailableHandler(IStaffUserRepository staffUsers)
{
    public async Task<Result<StaffUserSummary>> HandleAsync(Guid staffUserId, CancellationToken cancellationToken)
    {
        StaffUser? user = await staffUsers.GetForUpdateAsync(staffUserId, cancellationToken);

        if (user is null) return StaffUserErrors.NotFound;

        user.MarkAvailable();

        await staffUsers.SaveChangesAsync(cancellationToken);

        return StaffUserSummary.Of(user);
    }
}
