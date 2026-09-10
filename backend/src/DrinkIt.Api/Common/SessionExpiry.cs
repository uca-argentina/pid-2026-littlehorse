namespace DrinkIt.Api.Common;

/// <summary>
/// Carries "the token was valid, it just ran out" from the JwtBearer events to
/// the place that shapes the 401 body. Both look at the same request, but by
/// the time the response is written the exception that knew the difference is
/// long gone.
/// </summary>
/// <remarks>
/// ADR-0008: one token and no refresh token, so an expired session is a normal
/// end-of-shift event rather than an edge case. The PWA needs to tell it apart
/// from a rejected login to say something the bartender understands, and both
/// arrive as a 401.
/// </remarks>
internal static class SessionExpiry
{
    private const string ItemKey = "drinkit.session_expired";

    public static void Mark(HttpContext context) => context.Items[ItemKey] = true;

    public static bool HasExpired(HttpContext context) => context.Items.ContainsKey(ItemKey);
}
