using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Api.Tenancy;

/// <summary>
/// What <see cref="VenueResolutionMiddleware"/> does for a request, done for
/// each hub call: SignalR runs every hub method, and OnConnectedAsync, in a
/// scope of its own that no middleware ever saw, so without this the global
/// query filters would match nothing there (US-38, the tablet's beat).
/// </summary>
/// <remarks>
/// From the token and never from anything the call carries, like every staff
/// screen. Every hub today is a staff one; a hub for customers would have to
/// say where its venue comes from before it could rely on this.
/// </remarks>
internal sealed class VenueResolutionHubFilter : IHubFilter
{
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        Resolve(invocationContext.Context, invocationContext.ServiceProvider);

        return next(invocationContext);
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        Resolve(context.Context, context.ServiceProvider);

        return next(context);
    }

    // Left unresolved when the token carries no venue: the filters then match
    // nothing, and the call fails closed.
    private static void Resolve(HubCallerContext caller, IServiceProvider services)
    {
        if (Guid.TryParse(caller.User?.FindFirst(JwtClaims.Venue)?.Value, out Guid venueId)) services.GetRequiredService<CurrentVenue>().Resolve(venueId);
    }
}
