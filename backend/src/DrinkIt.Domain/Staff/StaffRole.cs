namespace DrinkIt.Domain.Staff;

/// <summary>
/// What someone is allowed to do inside a venue. There is no "bartender": the
/// KDS is the bar station's own account, shared by everyone preparing there,
/// because the tablet belongs to the work station and not to a person
/// (functional design, §11).
/// </summary>
/// <remarks>
/// Values are explicit and start at 1 so that default(StaffRole) is not a valid
/// role: an uninitialised value gets rejected instead of silently becoming an
/// administrator. <see cref="Kds"/> keeps the number Bartender had before the
/// rename, so stored rows need no migration.
/// </remarks>
public enum StaffRole
{
    Administrator = 1,
    Kds = 2,
    Waiter = 3,
}
