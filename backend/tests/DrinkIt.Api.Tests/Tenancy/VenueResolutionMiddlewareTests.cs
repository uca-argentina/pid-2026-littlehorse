using System.Security.Claims;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
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
    public async Task InvokeAsync_WhenTheTokenAndTheSlugDisagreeOnAStaffEndpoint_TheTokenWins()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: VenueFromToken, slug: "bar-alfa");

        Assert.Equal(VenueFromToken, current.Id);
    }

    /// <summary>
    /// The other half of the same rule, and the one that bites: a public page
    /// addressed by slug is about that slug, whoever happens to be carrying a
    /// token. An administrator of one venue opening another venue's menu was
    /// getting their own venue's products under the other venue's name.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenTheEndpointIsPublic_TheSlugWinsOverTheToken()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: VenueFromToken, slug: "bar-alfa", isPublic: true);

        Assert.Equal(VenueFromSlug, current.Id);
    }

    // What the customer's menu reads to name the venue on screen. Known only
    // when the slug resolved it: a token carries an id and no name.
    [Fact]
    public async Task InvokeAsync_WhenTheSlugResolved_RemembersWhoTheVenueIs()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: null, slug: "bar-alfa");

        Assert.Equal("Bar Alfa", current.Identity?.Name);
        Assert.Equal("bar-alfa", current.Identity?.Slug);
    }

    [Fact]
    public async Task InvokeAsync_WhenOnlyATokenResolvedIt_KnowsNoNameForIt()
    {
        CurrentVenue current = new();

        await Run(current, claimedVenue: VenueFromToken, slug: null);

        Assert.Null(current.Identity);
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

    private static async Task Run(
        CurrentVenue current,
        Guid? claimedVenue,
        string? slug,
        bool isPublic = false)
    {
        DefaultHttpContext context = new();

        if (isPublic)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new AllowAnonymousAttribute()),
                "public"));
        }

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
        public Task<VenueIdentity?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(
                slug == "bar-alfa" ? new VenueIdentity(VenueFromSlug, "Bar Alfa", slug) : null);
    }
}
