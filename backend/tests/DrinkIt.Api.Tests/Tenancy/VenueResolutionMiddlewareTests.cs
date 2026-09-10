using System.Security.Claims;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Tenancy;

public class VenueResolutionMiddlewareTests
{
    private static readonly Guid VenueFromToken = Guid.CreateVersion7();
    private static readonly Guid VenueFromSlug = Guid.CreateVersion7();

    [Fact]
    public async Task InvokeAsync_WhenTheRequestIsAuthenticated_TakesTheVenueFromTheToken()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: VenueFromToken, slug: null);

        Assert.Equal(VenueFromToken, current.Id);
    }

    [Fact]
    public async Task InvokeAsync_WhenThereIsOnlyASlug_TakesTheVenueFromTheRoute()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: null, slug: "bar-alfa");

        Assert.Equal(VenueFromSlug, current.Id);
    }

    // The rule from CLAUDE.md: staff get their venue from the token claim and
    // never from the URL. If the slug won here, a bartender could read another
    // venue's data by editing the address bar.
    [Fact]
    public async Task InvokeAsync_WhenTheTokenAndTheSlugDisagree_TheTokenWins()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: VenueFromToken, slug: "bar-alfa");

        Assert.Equal(VenueFromToken, current.Id);
    }

    [Fact]
    public async Task InvokeAsync_WhenThereIsNeither_LeavesTheVenueUnresolved()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: null, slug: null);

        Assert.Equal(Guid.Empty, current.Id);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheSlugDoesNotExist_LeavesTheVenueUnresolved()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: null, slug: "does-not-exist");

        Assert.Equal(Guid.Empty, current.Id);
    }

    private static async Task Run(CurrentVenue current, Guid? claimedVenue, string? slug)
    {
        DefaultHttpContext context = new();

        if (claimedVenue is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtClaims.Venue, claimedVenue.Value.ToString())],
                authenticationType: "test"));
        }

        if (slug is not null)
        {
            context.Request.RouteValues[VenueResolutionMiddleware.SlugRouteValue] = slug;
        }

        VenueResolutionMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, current, new FakeVenueLookup());
    }

    private sealed class FakeVenueLookup : IVenueLookup
    {
        public Task<Guid?> FindIdBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(slug == "bar-alfa" ? VenueFromSlug : (Guid?)null);
    }
}
