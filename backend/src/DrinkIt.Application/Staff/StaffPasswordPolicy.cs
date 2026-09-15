using DrinkIt.Application.Common;

namespace DrinkIt.Application.Staff;

/// <summary>
/// What a staff password has to be. Shared by the two use cases that set one,
/// so creating an account and resetting its password can never disagree about
/// what is acceptable.
/// </summary>
public static class StaffPasswordPolicy
{
    /// <summary>
    /// Long enough that it is not guessable, short enough that it can be handed
    /// over out loud in a noisy venue. It is the administrator who types it,
    /// not its owner, so there is nobody to remember a longer one.
    /// </summary>
    public const int MinLength = 8;

    public static readonly Error TooShort =
        new("staff.password_too_short", $"The password must be at least {MinLength} characters.");

    /// <summary>
    /// Trimmed before judging, so a field holding only spaces is rejected for
    /// what it is rather than counted as eight characters.
    /// </summary>
    public static bool IsLongEnough(string? password) =>
        (password ?? string.Empty).Trim().Length >= MinLength;
}
