using System.Security.Claims;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Cashier;

/// <summary>Where <see cref="TillHub"/> is mapped. Shared so nothing hardcodes it twice.</summary>
public static class TillHubRoute
{
    public const string Path = "/hubs/till";
}

/// <summary>
/// US-26: the till connects here and joins its own venue's group, taken from
/// the token and never from the request — the same rule as the bar's hub.
/// </summary>
[Authorize(Roles = nameof(StaffRole.Cashier))]
public sealed class TillHub : Hub
{
    /// <summary>The one message this hub ever sends: reload "Por cobrar".</summary>
    public const string TillChanged = "TillChanged";

    public override async Task OnConnectedAsync()
    {
        // Left open without a group, the till would sit there "connected" and
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
    public static string GroupFor(Guid venueId) => $"till:{venueId}";

    private static Guid? VenueIdOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirst(JwtClaims.Venue)?.Value, out Guid venueId) ? venueId : null;
}
