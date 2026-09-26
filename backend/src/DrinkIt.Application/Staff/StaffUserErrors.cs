using DrinkIt.Application.Common;

namespace DrinkIt.Application.Staff;

/// <summary>What the staff user in a request is, once it is found. No password travels back.</summary>
public sealed record StaffUserSummary(
    Guid Id,
    string Username,
    DrinkIt.Domain.Staff.StaffRole Role,
    bool IsActive,
    AuditInfo Audit)
{
    internal static StaffUserSummary Of(DrinkIt.Domain.Staff.StaffUser user) =>
        new(user.Id, user.Username, user.Role, user.IsActive, AuditInfo.Of(user));
}

public static class StaffUserErrors
{
    /// <summary>
    /// Nobody in this venue has that id. Whether the row exists in another venue
    /// is deliberately indistinguishable: the global query filter hides it, and
    /// saying otherwise would let an administrator probe for ids elsewhere.
    /// </summary>
    public static readonly Error NotFound =
        new("staff.not_found", "Nobody in this venue has that id.");

    /// <summary>
    /// The venue must never be left unable to administer itself. Nothing can
    /// create or promote a staff user without an administrator token, so a
    /// venue that loses its last active administrator has no way back in.
    /// </summary>
    /// <remarks>
    /// Counting what would be left, rather than refusing whoever asks to touch
    /// their own account: the role and the active flag travel in a token that
    /// lasts eight hours and is never re-checked (ADR-0008), so two
    /// administrators could otherwise deactivate each other and lock the venue
    /// out between them.
    /// </remarks>
    public static readonly Error LastAdministrator = new(
        "staff.last_administrator",
        "This is the venue's last active administrator, and it cannot be left without one.");
}
