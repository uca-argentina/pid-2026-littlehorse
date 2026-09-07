using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Authentication;

/// <summary>
/// Exactly what authentication needs about a staff user, and nothing else. Not
/// the aggregate: login neither mutates it nor has an invariant to protect.
/// </summary>
public sealed record StaffCredentials(
    Guid StaffUserId,
    Guid VenueId,
    string Username,
    string PasswordHash,
    StaffRole Role,
    bool IsActive);
