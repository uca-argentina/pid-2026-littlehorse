using DrinkIt.Application.Common;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

public sealed record RenameStaffUserCommand(Guid StaffUserId, string? DisplayName);

/// <summary>
/// US-36: gives an account the name people read instead of its username — a
/// KDS is "Barra Pista" on its board and on the customer's tracking. A blank
/// name takes it away, and the screens fall back to the username.
/// </summary>
public sealed class RenameStaffUserHandler(IStaffUserRepository staffUsers)
{
    public async Task<Result<StaffUserSummary>> HandleAsync(
        RenameStaffUserCommand command,
        CancellationToken cancellationToken)
    {
        StaffUser? user = await staffUsers.GetForUpdateAsync(command.StaffUserId, cancellationToken);

        if (user is null) return StaffUserErrors.NotFound;

        // A name too long is a broken invariant, and Rename throws; the API
        // turns that into a 400, as with every other rule of the account.
        user.Rename(command.DisplayName);

        await staffUsers.SaveChangesAsync(cancellationToken);

        return StaffUserSummary.Of(user);
    }
}
