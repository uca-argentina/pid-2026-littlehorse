using System.Security.Claims;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DrinkIt.Infrastructure.Kds;

/// <summary>Where <see cref="KdsHub"/> is mapped. Shared so nothing hardcodes it twice.</summary>
public static class KdsHubRoute
{
    public const string Path = "/hubs/kds";
}

/// <summary>
/// US-15: the bar's tablet connects here and joins its own bar's group (US-36)
/// — never told apart by anything the connection itself could set, since a hub
/// method has no route to read a slug from. The same rule as everywhere else
/// a staff screen resolves its venue and its bar: from the token, never from
/// the request.
/// </summary>
/// <remarks>
/// US-38: the tablet beats on connecting and every twenty seconds after, and
/// the last beat is what paying reads to leave out a bar that went quiet.
/// </remarks>
[Authorize(Roles = nameof(StaffRole.Kds))]
public sealed class KdsHub(KdsBeats beats, TimeProvider clock) : Hub
{
    /// <summary>The one message this hub ever sends: reload, something changed.</summary>
    public const string BoardChanged = "BoardChanged";

    /// <summary>What the tablet calls every twenty seconds to say it is still there (US-38).</summary>
    public const string Beat = "Beat";

    public override async Task OnConnectedAsync()
    {
        // Left open without a group, the tablet would sit there "connected"
        // and never hear a thing. Closing it is what makes the screen say so.
        if (VenueIdOf(Context.User) is not Guid venueId || KdsIdOf(Context.User) is not Guid kdsId)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(venueId, kdsId));
        await beats.RecordAsync(kdsId, clock.GetUtcNow(), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Still here. Nothing is answered, and a beat that comes without a bar in
    /// its token is dropped: OnConnectedAsync already closed that connection.
    /// </summary>
    [HubMethodName(Beat)]
    public async Task BeatAsync()
    {
        if (KdsIdOf(Context.User) is not Guid kdsId) return;

        await beats.RecordAsync(kdsId, clock.GetUtcNow(), Context.ConnectionAborted);
    }

    /// <summary>
    /// One bar of one venue. Not a table name or anything durable — SignalR
    /// groups are an in-memory routing label that exists only while
    /// connections are open. The venue stays in it so two venues can never
    /// share one, whatever the ids.
    /// </summary>
    public static string GroupFor(Guid venueId, Guid kdsId) => $"venue:{venueId}:kds:{kdsId}";

    private static Guid? VenueIdOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirst(JwtClaims.Venue)?.Value, out Guid venueId) ? venueId : null;

    private static Guid? KdsIdOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out Guid kdsId) ? kdsId : null;
}
