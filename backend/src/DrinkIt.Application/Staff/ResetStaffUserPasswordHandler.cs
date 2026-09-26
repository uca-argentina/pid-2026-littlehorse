using DrinkIt.Application.Common;
using DrinkIt.Application.Security;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

public sealed record ResetStaffUserPasswordCommand(Guid StaffUserId, string Password);

/// <summary>
/// Sets a new password for somebody who forgot theirs. Nothing can recover the
/// old one, because only its hash was ever stored: what makes it stop working
/// is that the hash is replaced.
/// </summary>
/// <remarks>
/// An administrator may reset their own, unlike changing a role or deactivating
/// an account. Somebody locked out of their own account is the one person who
/// cannot ask anybody else to fix it.
/// </remarks>
public sealed class ResetStaffUserPasswordHandler(
    IStaffUserRepository staffUsers,
    IPasswordHasher passwordHasher)
{
    public async Task<Result<StaffUserSummary>> HandleAsync(
        ResetStaffUserPasswordCommand command,
        CancellationToken cancellationToken)
    {
        if (!StaffPasswordPolicy.IsLongEnough(command.Password)) return StaffPasswordPolicy.TooShort;

        StaffUser? user = await staffUsers.GetForUpdateAsync(command.StaffUserId, cancellationToken);

        if (user is null) return StaffUserErrors.NotFound;

        user.ChangePassword(passwordHasher.Hash(command.Password));

        await staffUsers.SaveChangesAsync(cancellationToken);

        return StaffUserSummary.Of(user);
    }
}
