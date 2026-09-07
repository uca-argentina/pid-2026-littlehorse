namespace DrinkIt.Domain.Staff;

/// <summary>
/// Values are explicit and start at 1 so that default(StaffRole) is not a valid
/// role: an uninitialised value gets rejected instead of silently becoming an
/// administrator.
/// </summary>
public enum StaffRole
{
    Administrator = 1,
    Bartender = 2,
}
