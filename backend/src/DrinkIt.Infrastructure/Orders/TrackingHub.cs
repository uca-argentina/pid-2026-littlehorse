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
    /// The key in <see cref="HubCallerContext.Items"/> holding the group this
    /// connection follows. There and not in a field: SignalR creates a new hub
    /// instance for every call, and the items live as long as the connection.
    /// </summary>
    private const string FollowedGroup = "followed-group";

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

        // One order per connection, like the screen: following another lets go
        // of the last, so no connection piles up groups the server keeps.
        if (Context.Items[FollowedGroup] is string previous) await Groups.RemoveFromGroupAsync(Context.ConnectionId, previous);

        string followed = GroupFor(order!);
        Context.Items[FollowedGroup] = followed;
        await Groups.AddToGroupAsync(Context.ConnectionId, followed);
    }

    /// <summary>
    /// A hash of the token rather than the token: group names are routing
    /// labels that end up in diagnostics, and the secret has no business there.
    /// </summary>
    public static string GroupFor(TrackingToken order) =>
        $"order:{Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(order.Value)))}";
}
