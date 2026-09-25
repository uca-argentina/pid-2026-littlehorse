using DrinkIt.Api.Features.Menu;
using DrinkIt.Api.Tenancy;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Venues;
using DrinkIt.Domain.Menu;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Menu;

/// <summary>
/// US-09. The only endpoint of the app that answers without a token, so the
/// shape of what it sends back is the contract a stranger's phone gets.
/// </summary>
public class MenuEndpointTests
{
    private const string Path = "/bar-alfa/menu";

    private static readonly MenuItem GinTonic = new(
        Guid.CreateVersion7(), "Gin Tonic", "Gin, tónica, lima", "https://images.example.com/gin.png", 4500m,
        ProductCategory.Drink, true);

    private static readonly MenuItem Aperol = new(
        Guid.CreateVersion7(), "Aperol Spritz", null, null, 6000m, ProductCategory.Drink, false);

    // Pins the shape: the Angular client is generated from it, so renaming a
    // property here breaks the customer's screen silently.
    [Fact]
    public async Task GetAsync_WhenTheVenueSellsSomething_RespondsWithItsMenu()
    {
        HttpResponseSnapshot response = await Menu(GinTonic, Aperol);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal(2, response.Body.GetProperty("items").GetArrayLength());
        Assert.Equal("Gin Tonic", response.Body.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(4500m, response.Body.GetProperty("items")[0].GetProperty("price").GetDecimal());
        Assert.Equal("Drink", response.Body.GetProperty("items")[0].GetProperty("category").GetString());
        Assert.False(response.Body.GetProperty("items")[1].GetProperty("isOrderable").GetBoolean());
    }

    // The header says which venue this is, and the customer scanned a QR: they
    // never typed the name, so the screen has to be the one that tells them.
    [Fact]
    public async Task GetAsync_WhenTheVenueExists_NamesIt()
    {
        HttpResponseSnapshot response = await Menu(GinTonic);

        Assert.Equal("Bar Alfa", response.Text("venueName"));
    }

    // Criterion 5 is the screen's job, but it can only do it if an empty venue
    // answers with an empty menu rather than with a failure.
    [Fact]
    public async Task GetAsync_WhenNothingIsLoadedYet_RespondsWithAnEmptyMenu()
    {
        HttpResponseSnapshot response = await Menu();

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(0, response.Body.GetProperty("items").GetArrayLength());
        Assert.Equal("Bar Alfa", response.Text("venueName"));
    }

    /// <summary>
    /// A mistyped or invented slug. Answering 404 keeps it apart from a venue
    /// that exists and has nothing loaded, which is criterion 5 and reads
    /// completely differently on the phone.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenNoVenueHasThatSlug_RespondsWithNotFound()
    {
        IResult result = await MenuEndpoint.GetAsync(
            "bar-que-no-existe", new CurrentVenue(), new Fake.Menu(), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:venue:not-found", response.Text("type"));
    }

    // Nothing about how many are left leaves the building.
    [Fact]
    public async Task GetAsync_WhenTheVenueSellsSomething_SendsNoStockCount()
    {
        HttpResponseSnapshot response = await Menu(GinTonic, Aperol);

        Assert.DoesNotContain("stock", response.Raw, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseSnapshot> Menu(params MenuItem[] items)
    {
        CurrentVenue venue = new();
        venue.Resolve(new VenueIdentity(Guid.CreateVersion7(), "Bar Alfa", "bar-alfa"));

        IResult result = await MenuEndpoint.GetAsync(
            "bar-alfa", venue, new Fake.Menu(items), CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Get);
    }

    private static class Fake
    {
        public sealed class Menu(params MenuItem[] items) : IProductQueries
        {
            public Task<IReadOnlyList<ProductListItem>> ListAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException("The customer's menu never reads the administration listing.");

            public Task<IReadOnlyList<MenuItem>> ListForMenuAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<MenuItem>>(items);
        }
    }
}
