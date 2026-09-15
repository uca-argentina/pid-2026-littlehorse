using System.Text.Json;
using DrinkIt.Api.Features.Menu;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Menu;

public class ProductsEndpointsTests
{
    private const string Path = "/staff/products";

    private static CreateProductRequest AGinTonic(string name = "Gin Tonic", decimal price = 4500m, int stock = 20) =>
        new(name, "Gin, tonic and a slice of lime.", "https://images.example.com/gin-tonic.jpg", price, stock);

    // Pins the success shape: the Angular client is generated from it, so
    // renaming a property here breaks the screen silently.
    [Fact]
    public async Task CreateAsync_WhenTheDataIsValid_RespondsWithTheCreatedProduct()
    {
        HttpResponseSnapshot response = await Create(AGinTonic(stock: 0));

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, response.Body.GetProperty("id").GetGuid());
        Assert.Equal("Gin Tonic", response.Text("name"));
        Assert.Equal("Gin, tonic and a slice of lime.", response.Text("description"));
        Assert.Equal("https://images.example.com/gin-tonic.jpg", response.Text("imageUrl"));
        Assert.Equal(4500m, response.Body.GetProperty("price").GetDecimal());
        Assert.Equal(0, response.Body.GetProperty("stock").GetInt32());
        Assert.True(response.Body.GetProperty("isAvailable").GetBoolean());
        Assert.True(response.Body.GetProperty("isSoldOut").GetBoolean());
        Assert.True(response.Body.GetProperty("isActive").GetBoolean());
    }

    // The optional fields travel as null, not as an empty string and not
    // omitted: the generated client types them as nullable and the listing
    // branches on null to show the placeholder.
    [Fact]
    public async Task CreateAsync_WhenTheOptionalFieldsAreMissing_RespondsWithThemAsNull()
    {
        HttpResponseSnapshot response = await Create(new CreateProductRequest("Gin Tonic", null, null, 4500m, 20));

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("imageUrl").ValueKind);
    }

    [Fact]
    public async Task CreateAsync_WhenTheNameIsAlreadyUsedInThisVenue_RespondsWithConflict()
    {
        HttpResponseSnapshot response = await Create(AGinTonic(), taken: "Gin Tonic");

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:product:name-taken", response.Text("type"));
    }

    // US-06, criterion 2. The endpoint does not check the price itself: the
    // domain throws and the global handler answers. This pins that the
    // exception gets out of the endpoint untouched, with its code intact.
    [Fact]
    public async Task CreateAsync_WhenThePriceIsNotPositive_LetsTheDomainExceptionThrough()
    {
        DomainException error = await Assert.ThrowsAsync<DomainException>(() => Create(AGinTonic(price: 0)));

        Assert.Equal(Product.ErrorCodes.PriceNotPositive, error.Code);
    }

    [Fact]
    public async Task ListAsync_WhenTheVenueHasProducts_RespondsWithAllOfThem()
    {
        ProductListItem[] stored =
        [
            new(Guid.CreateVersion7(), "Aperol Spritz", "Aperol, prosecco, soda", null, 5200m, 0, IsAvailable: true, IsSoldOut: true, IsActive: true),
            new(Guid.CreateVersion7(), "Gin Tonic", null, "https://images.example.com/gin-tonic.jpg", 4500m, 20, IsAvailable: false, IsSoldOut: false, IsActive: false),
        ];

        IResult result = await ProductsEndpoints.ListAsync(new Fake.Queries(stored), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(2, response.Body.GetArrayLength());
        Assert.Equal("Aperol Spritz", response.Body[0].GetProperty("name").GetString());
        Assert.True(response.Body[0].GetProperty("isSoldOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, response.Body[0].GetProperty("imageUrl").ValueKind);
        Assert.Equal(20, response.Body[1].GetProperty("stock").GetInt32());
        Assert.False(response.Body[1].GetProperty("isAvailable").GetBoolean());
        Assert.False(response.Body[1].GetProperty("isActive").GetBoolean());
    }

    private static async Task<HttpResponseSnapshot> Create(CreateProductRequest request, string? taken = null)
    {
        CreateProductHandler handler = new(new Fake.Repository(taken), new Fake.CurrentVenue());

        IResult result = await ProductsEndpoints.CreateAsync(request, handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Post);
    }

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id { get; } = Guid.CreateVersion7();
        }

        public sealed class Repository(string? taken) : IProductRepository
        {
            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(string.Equals(name, taken, StringComparison.OrdinalIgnoreCase));

            public Task AddAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public sealed class Queries(ProductListItem[] stored) : IProductQueries
        {
            public Task<IReadOnlyList<ProductListItem>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ProductListItem>>(stored);
        }
    }
}
