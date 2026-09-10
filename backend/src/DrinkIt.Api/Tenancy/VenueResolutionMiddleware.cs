using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Authentication;

namespace DrinkIt.Api.Tenancy;

/// <summary>
/// Decides which venue the request belongs to, before anything queries the
/// database. Must run after authentication and after routing, so both the token
/// claims and the route values are available.
/// </summary>
internal sealed class VenueResolutionMiddleware(RequestDelegate next)
{
    internal const string SlugRouteValue = "venueSlug";

    public async Task InvokeAsync(HttpContext context, CurrentVenue currentVenue, IVenueLookup venues)
    {
        Guid? venueId = FromToken(context) ?? await FromRouteSlug(context, venues);

        // Left unresolved on purpose when neither source answers: the global
        // query filter then matches nothing, so the request fails closed.
        if (venueId is not null) currentVenue.Resolve(venueId.Value);

        await next(context);
    }

    /// <summary>
    /// Checked first, and it wins over the slug. CLAUDE.md: staff take their
    /// venue from the token and never from the URL, so an authenticated
    /// bartender cannot reach another venue by editing the address bar.
    /// </summary>
    private static Guid? FromToken(HttpContext context)
    {
        string? claimed = context.User.FindFirst(JwtClaims.Venue)?.Value;

        return Guid.TryParse(claimed, out Guid venueId) ? venueId : null;
    }

    /// <summary>
    /// The customer's path: the slug comes from the QR they scanned. Anonymous,
    /// so there is no token to trust yet.
    /// </summary>
    private static async Task<Guid?> FromRouteSlug(HttpContext context, IVenueLookup venues)
    {
        if (context.Request.RouteValues.GetValueOrDefault(SlugRouteValue) is not string slug) return null;

        // TODO: venues change about never and this runs on every request, so it
        // is worth caching once there is more than one of them.
        return await venues.FindIdBySlugAsync(slug, context.RequestAborted);
    }
}
