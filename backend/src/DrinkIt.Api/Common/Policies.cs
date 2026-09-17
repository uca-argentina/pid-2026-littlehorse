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
}
