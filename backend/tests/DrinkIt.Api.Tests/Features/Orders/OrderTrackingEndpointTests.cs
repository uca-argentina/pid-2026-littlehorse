using DrinkIt.Api.Features.Orders;
using DrinkIt.Api.Tenancy;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Venues;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Orders;

/// <summary>
/// US-12. The third endpoint that answers without a token of the staff kind,
/// and the one that decides whether a stranger gets to read somebody's order.
/// </summary>
public class OrderTrackingEndpointTests
{
    private const string Token = "9f3c2ba7d81e4c06a1b2c3d4e5f60718";

    private const string Path = "/bar-alfa/orders/K-4821/" + Token;

    private static readonly TrackedOrder Waiting = new(
        "K-4821",
        "María Quadro",
        OrderStatus.Queued,
        9000m,
        new DateTimeOffset(2026, 9, 17, 2, 30, 0, TimeSpan.Zero),
        [new TrackedOrderItem("Gin Tonic", 2, "sin hielo")]);

    // Pins the shape: the Angular client is generated from it, and this is
    // fetched every three seconds by every phone in the venue.
    [Fact]
    public async Task FollowAsync_WhenTheLinkIsRight_SaysWhereTheOrderIs()
    {
        HttpResponseSnapshot response = await Follow(Waiting);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("K-4821", response.Text("code"));
        Assert.Equal("Queued", response.Text("status"));
        Assert.Equal("María Quadro", response.Text("customerName"));
        Assert.Equal(9000m, response.Body.GetProperty("total").GetDecimal());
        Assert.Equal("Gin Tonic", response.Body.GetProperty("items")[0].GetProperty("productName").GetString());
    }

    /// <summary>
    /// The secret never comes back out. The screen already holds it, and this
    /// answer passes through caches and logs on its way to a phone.
    /// </summary>
    [Fact]
    public async Task FollowAsync_WhenItAnswers_NeverSendsTheTokenBack()
    {
        HttpResponseSnapshot response = await Follow(Waiting);

        Assert.DoesNotContain(Token, response.Raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", response.Raw, StringComparison.OrdinalIgnoreCase);
    }

    // Criterion 5: every way of not getting in looks the same from outside. A
    // 403 would confirm that the code exists, which is the half worth hiding.
    [Fact]
    public async Task FollowAsync_WhenTheLinkLeadsNowhere_RespondsWithNotFound()
    {
        HttpResponseSnapshot response = await Follow(null);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:order:not-found", response.Text("type"));
    }

    [Fact]
    public async Task FollowAsync_WhenNoVenueHasThatSlug_RespondsWithNotFound()
    {
        IResult result = await OrderTrackingEndpoint.FollowAsync(
            "bar-que-no-existe", "K-4821", Token, new CurrentVenue(), new Fake.Tracking(Waiting),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:not-found", response.Text("type"));
    }

    // Nothing about how many are left, here either.
    [Fact]
    public async Task FollowAsync_WhenTheLinkIsRight_SendsNoStockCount()
    {
        HttpResponseSnapshot response = await Follow(Waiting);

        Assert.DoesNotContain("stock", response.Raw, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseSnapshot> Follow(TrackedOrder? found)
    {
        CurrentVenue venue = new();
        venue.Resolve(new VenueIdentity(Guid.CreateVersion7(), "Bar Alfa", "bar-alfa"));

        IResult result = await OrderTrackingEndpoint.FollowAsync(
            "bar-alfa", "K-4821", Token, venue, new Fake.Tracking(found), CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Get);
    }

    private static class Fake
    {
        public sealed class Tracking(TrackedOrder? found) : IOrderTrackingQueries
        {
            public Task<TrackedOrder?> FindAsync(
                string code,
                string? token,
                CancellationToken cancellationToken) =>
                Task.FromResult(found);
        }
    }
}
