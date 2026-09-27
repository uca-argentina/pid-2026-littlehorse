using DrinkIt.Application.Common;
using DrinkIt.Application.Security;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

public sealed record CreateStaffUserCommand(string Username, string Password, StaffRole Role);

/// <summary>The new user as the listing shows it. No password travels back, hashed or not.</summary>
public sealed record CreatedStaffUser(Guid Id, string Username, StaffRole Role, bool IsActive, AuditInfo Audit);

/// <summary>
/// Adds someone to the venue the signed-in administrator belongs to. The venue
/// comes from <see cref="ICurrentVenue"/>, which reads the token claim, so an
/// administrator cannot create staff anywhere else by changing the request.
/// </summary>
public sealed class CreateStaffUserHandler(
    IStaffUserRepository staffUsers,
    IPasswordHasher passwordHasher,
    ICurrentVenue currentVenue)
{
    public static readonly Error UsernameTaken =
        new("staff.username_taken", "Somebody in this venue already uses that username.");

    public async Task<Result<CreatedStaffUser>> HandleAsync(
        CreateStaffUserCommand command,
        CancellationToken cancellationToken)
    {
        // Normalised here, before the availability check, so "Martin.P" and
        // "martin.p" cannot both get past it and collide on the unique index
        // afterwards, where the failure is a 500 and not a readable message.
        string username = StaffUser.NormalizeUsername(command.Username);

        if (!StaffPasswordPolicy.IsLongEnough(command.Password)) return StaffPasswordPolicy.TooShort;
        if (await staffUsers.UsernameExistsAsync(username, cancellationToken)) return UsernameTaken;

        // Whatever is left wrong with the username or the role is a broken
        // domain invariant, and Create throws. The API turns that into a 400;
        // restating the rules here would be a second copy that drifts.
        StaffUser user = StaffUser.Create(
            currentVenue.Id,
            username,
            passwordHasher.Hash(command.Password!),
            command.Role);

        await staffUsers.AddAsync(user, cancellationToken);

        return new CreatedStaffUser(user.Id, user.Username, user.Role, user.IsActive, AuditInfo.Of(user));
    }
}
