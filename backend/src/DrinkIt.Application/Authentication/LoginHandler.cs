using DrinkIt.Application.Common;
using DrinkIt.Application.Security;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Authentication;

public sealed record LoginCommand(string Username, string Password);

public sealed record LoggedInStaff(Guid StaffUserId, Guid VenueId, string Username, StaffRole Role);

public sealed class LoginHandler(IStaffCredentialsQuery credentials, IPasswordHasher passwordHasher)
{
    /// <summary>
    /// The single failure every rejected login returns. Distinguishing "no such
    /// user" from "wrong password" would hand an attacker a way to enumerate
    /// which usernames exist in a venue.
    /// </summary>
    public static readonly Error InvalidCredentials =
        new("auth.invalid_credentials", "Username or password is incorrect.");

    public async Task<Result<LoggedInStaff>> HandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        StaffCredentials? staff = await credentials.FindAsync(command.Username, cancellationToken);

        if (staff is null) return RejectAfterSpendingTheSameTime(command.Password);
        if (!staff.IsActive) return InvalidCredentials;
        if (!passwordHasher.Verify(command.Password, staff.PasswordHash)) return InvalidCredentials;

        return new LoggedInStaff(staff.StaffUserId, staff.VenueId, staff.Username, staff.Role);
    }

    /// <summary>
    /// Runs the key derivation even though there is nothing to compare against.
    /// Returning early here would answer in microseconds while a real username
    /// costs the full derivation, and that gap alone reveals which usernames
    /// exist.
    /// </summary>
    private Error RejectAfterSpendingTheSameTime(string password)
    {
        _ = passwordHasher.Hash(password);

        return InvalidCredentials;
    }
}
