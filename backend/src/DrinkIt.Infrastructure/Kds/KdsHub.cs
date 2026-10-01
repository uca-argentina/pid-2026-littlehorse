using System.Security.Claims;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Kds;

/// <summary>Where <see cref="KdsHub"/> is mapped. Shared so nothing hardcodes it twice.</summary>
public static class KdsHubRoute
{
    public const string Path = "/hubs/kds";
}

/// <summary>
/// US-15: the bar's tablet connects here and joins its own venue's group —
/// never told apart by anything the connection itself could set, since a hub
/// method has no route to read a slug from. The same rule as everywhere else
/// a staff screen resolves its venue: from the token, never from the request.
/// </summary>
[Authorize(Roles = nameof(StaffRole.Kds))]
public sealed class KdsHub : Hub
{
    /// <summary>The one message this hub ever sends: reload, something changed.</summary>
    public const string BoardChanged = "BoardChanged";

    public override async Task OnConnectedAsync()
    {
        // Left open without a group, the tablet would sit there "connected"
        // and never hear a thing. Closing it is what makes the screen say so.
        if (VenueIdOf(Context.User) is not Guid venueId)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(venueId));
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Not a per-venue table name or anything durable — SignalR groups are an
    /// in-memory routing label that exists only while connections are open.
    /// </summary>
    public static string GroupFor(Guid venueId) => $"venue:{venueId}";

    private static Guid? VenueIdOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirst(JwtClaims.Venue)?.Value, out Guid venueId) ? venueId : null;
}
