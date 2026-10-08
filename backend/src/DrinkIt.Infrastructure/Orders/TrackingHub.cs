using System.Security.Cryptography;
using System.Text;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Orders;

/// <summary>Where <see cref="TrackingHub"/> is mapped. Shared so nothing hardcodes it twice.</summary>
public static class TrackingHubRoute
{
    public const string Path = "/hubs/tracking";
}

/// <summary>
/// US-22: the customer's tracking screen connects here and follows its order,
/// so it hears the order move instead of asking every few seconds.
/// </summary>
[AllowAnonymous]
public sealed class TrackingHub : Hub
{
    /// <summary>The one message this hub ever sends: ask again, your order moved.</summary>
    public const string OrderChanged = "OrderChanged";

    /// <summary>
    /// How many orders one connection follows at once: the menu follows every
    /// order of the night (US-34), and a night is a few rounds, not dozens.
    /// The cap is what stops one connection from joining groups without end.
    /// </summary>
    public const int MostFollowedPerConnection = 5;

    /// <summary>
    /// The key in <see cref="HubCallerContext.Items"/> holding the groups this
    /// connection follows, oldest first. There and not in a field: SignalR
    /// creates a new hub instance for every call, and the items live as long
    /// as the connection.
    /// </summary>
    private const string FollowedGroups = "followed-groups";

    /// <summary>
    /// Joins the order this token belongs to. Checked against nothing, on
    /// purpose: the token is the proof, the same one the tracking link asks
    /// for, and all it earns here is a nudge to go ask that link. Somebody
    /// guessing tokens gains nothing a wrong guess on the link would not.
    /// </summary>
    /// <remarks>
    /// Nothing is looked up either, so a hundred phones connecting at once in a
    /// packed venue cost the database nothing. Invoked rather than read from
    /// the connection's address, which is what proxies and logs keep.
    /// </remarks>
    public async Task Follow(string token)
    {
        // A link mangled while copying it is an answer, not a crash.
        if (!TrackingToken.TryParse(token, out TrackingToken? order)) return;

        if (Context.Items[FollowedGroups] is not List<string> followed)
        {
            followed = [];
            Context.Items[FollowedGroups] = followed;
        }

        string group = GroupFor(order!);
        if (followed.Contains(group)) return;

        // Full: the oldest makes room, so no connection piles up groups the server keeps.
        if (followed.Count == MostFollowedPerConnection)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, followed[0]);
            followed.RemoveAt(0);
        }

        followed.Add(group);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    /// <summary>
    /// A hash of the token rather than the token: group names are routing
    /// labels that end up in diagnostics, and the secret has no business there.
    /// </summary>
    public static string GroupFor(TrackingToken order) =>
        $"order:{Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(order.Value)))}";
}
