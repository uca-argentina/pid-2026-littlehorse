using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Authentication;

namespace DrinkIt.Api.Tenancy;

/// <summary>
/// Declares that an endpoint is addressed by the slug in its own route — the
/// customer's menu, the staff login — and must resolve its venue from that
/// slug rather than from a token. Deliberately its own metadata and not a
/// reuse of <see cref="Microsoft.AspNetCore.Authorization.IAllowAnonymous"/>:
/// authentication and tenancy resolution are different questions that happen
/// to agree on today's two endpoints, and a future anonymous endpoint that is
/// not about any particular venue (a health check, say) must not silently
/// inherit slug-resolution semantics it was never meant to have.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ScopedBySlugAttribute : Attribute;

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
/// An endpoint marked <see cref="ScopedBySlugAttribute"/> is addressed by slug
/// and belongs to that slug, whoever happens to be carrying a token. Otherwise
/// an administrator of one venue opening another venue's menu would be served
/// their own venue's products under the other venue's name.
/// </item>
/// </list>
/// </remarks>
internal sealed class VenueResolutionMiddleware(RequestDelegate next)
{
    internal const string SlugRouteValue = "venueSlug";

    public async Task InvokeAsync(HttpContext context, CurrentVenue currentVenue, IVenueLookup venues)
    {
        if (IsScopedBySlug(context))
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
    /// Declared, not inferred: routing has already run, so the metadata is
    /// here. See <see cref="ScopedBySlugAttribute"/> for why this is its own
    /// declaration instead of a check on anonymous access.
    /// </summary>
    private static bool IsScopedBySlug(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ScopedBySlugAttribute>() is not null;

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
