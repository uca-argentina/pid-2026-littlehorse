using System.Security.Claims;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Waiter;

/// <summary>Where <see cref="WaiterHub"/> is mapped. Shared so nothing hardcodes it twice.</summary>
public static class WaiterHubRoute
{
    public const string Path = "/hubs/waiter";
}

/// <summary>
/// US-43: the waiter's phone connects here and joins its own venue's group,
/// taken from the token and never from the request — the same rule as the
/// bar's and the till's hubs.
/// </summary>
[Authorize(Roles = nameof(StaffRole.Waiter))]
public sealed class WaiterHub : Hub
{
    /// <summary>The one message this hub ever sends: reload the list of tables.</summary>
    public const string TablesChanged = "TablesChanged";

    public override async Task OnConnectedAsync()
    {
        // Left open without a group, the phone would sit there "connected" and
        // never hear a thing. Closing it is what makes the screen say so.
        if (VenueIdOf(Context.User) is not Guid venueId)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(venueId));
        await base.OnConnectedAsync();
    }

    /// <summary>An in-memory routing label, alive only while connections are open.</summary>
    public static string GroupFor(Guid venueId) => $"waiters:{venueId}";

    private static Guid? VenueIdOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirst(JwtClaims.Venue)?.Value, out Guid venueId) ? venueId : null;
}
