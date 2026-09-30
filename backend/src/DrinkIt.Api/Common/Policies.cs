using DrinkIt.Domain.Staff;

namespace DrinkIt.Api.Common;

/// <summary>
/// Authorization policies by name. Endpoints name a policy instead of a role
/// so that widening one later is a change in Program.cs and not a sweep through
/// every endpoint.
/// </summary>
internal static class Policies
{
    /// <summary>
    /// The administration screens: the staff ABM and, later, the menu. Written
    /// as "only the administrator" rather than "everyone except the KDS", so a
    /// role added afterwards is locked out until somebody decides otherwise.
    /// </summary>
    public const string Administrator = nameof(StaffRole.Administrator);

    /// <summary>The bar's own tablet (US-15): only the station's own account reads its queue.</summary>
    public const string Kds = nameof(StaffRole.Kds);

    /// <summary>The till (US-26): looks up what is waiting for cash and takes it.</summary>
    public const string Cashier = nameof(StaffRole.Cashier);
}
