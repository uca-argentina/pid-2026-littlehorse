using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace DrinkIt.Api.Tenancy;

/// <summary>
/// Decides which venue the request belongs to, before anything queries the
/// database. Must run after authentication and after routing, so both the token
/// claims and the route values are available.
/// </summary>
/// <remarks>
/// Which source wins depends on who the endpoint is for, and both halves come
/// from the same rule in CLAUDE.md.
/// <list type="bullet">
/// <item>
/// A staff endpoint takes the venue from the token and never from the URL, so
/// an authenticated bartender cannot reach another venue by editing the address.
/// </item>
/// <item>
/// A public endpoint is addressed by slug and belongs to that slug, whoever
/// happens to be carrying a token. Otherwise an administrator of one venue
/// opening another venue's menu would be served their own venue's products
/// under the other venue's name.
/// </item>
/// </list>
/// </remarks>
internal sealed class VenueResolutionMiddleware(RequestDelegate next)
{
    internal const string SlugRouteValue = "venueSlug";

    public async Task InvokeAsync(HttpContext context, CurrentVenue currentVenue, IVenueLookup venues)
    {
        if (IsPublic(context))
        {
            VenueIdentity? fromSlug = await FromRouteSlug(context, venues);

            if (fromSlug is not null) currentVenue.Resolve(fromSlug);
        }
        else if (FromToken(context) is Guid claimed)
        {
            currentVenue.Resolve(claimed);
        }
        else if (await FromRouteSlug(context, venues) is VenueIdentity fromSlug)
        {
            currentVenue.Resolve(fromSlug);
        }

        // Left unresolved on purpose when nothing answers: the global query
        // filter then matches nothing, so the request fails closed.
        await next(context);
    }

    /// <summary>
    /// Anonymous by declaration, which is how an endpoint says it is the
    /// customer's door. Routing has already run, so the metadata is here.
    /// </summary>
    private static bool IsPublic(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    private static Guid? FromToken(HttpContext context)
    {
        string? claimed = context.User.FindFirst(JwtClaims.Venue)?.Value;

        return Guid.TryParse(claimed, out Guid venueId) ? venueId : null;
    }

    /// <summary>The customer's path: the slug comes from the QR they scanned.</summary>
    private static async Task<VenueIdentity?> FromRouteSlug(HttpContext context, IVenueLookup venues)
    {
        if (context.Request.RouteValues.GetValueOrDefault(SlugRouteValue) is not string slug) return null;

        // TODO: venues change about never and this runs on every request, so it
        // is worth caching once there is more than one of them.
        return await venues.FindBySlugAsync(slug, context.RequestAborted);
    }
}
