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

    // US-07, criterion 1: the client sees an unavailable product dimmed, not
    // gone — so the endpoint hands back the updated product, not a bare 204.
    [Fact]
    public async Task MarkUnavailableAsync_WhenTheProductExists_RespondsWithItMarkedUnavailable()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);
        MarkProductUnavailableHandler handler = new(new Fake.Repository(null, product));

        IResult result = await ProductsEndpoints.MarkUnavailableAsync(product.Id, handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{product.Id}/mark-unavailable", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.False(response.Body.GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task MarkUnavailableAsync_WhenTheProductIsNotInThisVenue_RespondsWithNotFound()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);
        MarkProductUnavailableHandler handler = new(new Fake.Repository(null, product));

        IResult result = await ProductsEndpoints.MarkUnavailableAsync(Guid.CreateVersion7(), handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{Guid.CreateVersion7()}/mark-unavailable", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:not-found", response.Text("type"));
    }

    [Fact]
    public async Task MarkAvailableAsync_WhenTheProductExists_RespondsWithItMarkedAvailable()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);
        product.MarkUnavailable();
        MarkProductAvailableHandler handler = new(new Fake.Repository(null, product));

        IResult result = await ProductsEndpoints.MarkAvailableAsync(product.Id, handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{product.Id}/mark-available", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.True(response.Body.GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task MarkAvailableAsync_WhenTheProductIsNotInThisVenue_RespondsWithNotFound()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);
        MarkProductAvailableHandler handler = new(new Fake.Repository(null, product));

        IResult result = await ProductsEndpoints.MarkAvailableAsync(Guid.CreateVersion7(), handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{Guid.CreateVersion7()}/mark-available", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:not-found", response.Text("type"));
    }

    // The switch cannot undo running out. The endpoint does not check it: the
    // domain throws and the global handler answers with the rule's own type.
    [Fact]
    public async Task MarkAvailableAsync_WhenTheProductRanOut_LetsTheDomainExceptionThrough()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 0);
        MarkProductAvailableHandler handler = new(new Fake.Repository(null, product));

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => ProductsEndpoints.MarkAvailableAsync(product.Id, handler, CancellationToken.None));

        Assert.Equal(Product.ErrorCodes.SoldOutCannotBeAvailable, error.Code);
    }

    private static UpdateProductRequest AnUpdate(string name = "Fernet con Coca", decimal price = 3800m) =>
        new(name, "Medida doble.", price);

    // US-08, criterion 1. Pins the success shape for the same reason as
    // CreateAsync above.
    [Fact]
    public async Task UpdateAsync_WhenTheDataIsValid_RespondsWithTheUpdatedProduct()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);

        HttpResponseSnapshot response = await Update(product, product.Id, AnUpdate());

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("Fernet con Coca", response.Text("name"));
        Assert.Equal("Medida doble.", response.Text("description"));
        Assert.Equal(3800m, response.Body.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task UpdateAsync_WhenTheProductIsNotInThisVenue_RespondsWithNotFound()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);

        HttpResponseSnapshot response = await Update(product, Guid.CreateVersion7(), AnUpdate());

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:not-found", response.Text("type"));
    }

    [Fact]
    public async Task UpdateAsync_WhenTheNewNameIsAlreadyUsedInThisVenue_RespondsWithConflict()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);

        HttpResponseSnapshot response = await Update(product, product.Id, AnUpdate(name: "Fernet con Coca"), taken: "Fernet con Coca");

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:name-taken", response.Text("type"));
    }

    // US-08, criterion 2. Same shape as CreateAsync's equivalent test: the
    // domain throws and the exception gets out untouched.
    [Fact]
    public async Task UpdateAsync_WhenThePriceIsNotPositive_LetsTheDomainExceptionThrough()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => Update(product, product.Id, AnUpdate(price: 0)));

        Assert.Equal(Product.ErrorCodes.PriceNotPositive, error.Code);
    }

    // US-08, criterion 3.
    [Fact]
    public async Task DeactivateAsync_WhenTheProductExists_RespondsWithItDeactivated()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);
        DeactivateProductHandler handler = new(new Fake.Repository(null, product));

        IResult result = await ProductsEndpoints.DeactivateAsync(product.Id, handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{product.Id}/deactivate", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.False(response.Body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task DeactivateAsync_WhenTheProductIsNotInThisVenue_RespondsWithNotFound()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);
        DeactivateProductHandler handler = new(new Fake.Repository(null, product));

        IResult result = await ProductsEndpoints.DeactivateAsync(Guid.CreateVersion7(), handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{Guid.CreateVersion7()}/deactivate", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:not-found", response.Text("type"));
    }

    private static async Task<HttpResponseSnapshot> Update(Product stored, Guid productId, UpdateProductRequest request, string? taken = null)
    {
        UpdateProductHandler handler = new(new Fake.Repository(taken, stored));

        IResult result = await ProductsEndpoints.UpdateAsync(productId, request, handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, $"{Path}/{productId}", HttpMethods.Put);
    }

    // Bringing stock back: the response is the product, so the screen shows
    // the new count and unlocks the nightly switch without asking again.
    [Fact]
    public async Task RestockAsync_WhenTheUnitsAreValid_RespondsWithTheProductWithMoreStock()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 0);
        RestockProductHandler handler = new(new Fake.Repository(null, product));

        IResult result = await ProductsEndpoints.RestockAsync(product.Id, new RestockProductRequest(12), handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{product.Id}/restock", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(12, response.Body.GetProperty("stock").GetInt32());
        Assert.False(response.Body.GetProperty("isSoldOut").GetBoolean());
    }

    [Fact]
    public async Task RestockAsync_WhenTheProductIsNotInThisVenue_RespondsWithNotFound()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 0);
        RestockProductHandler handler = new(new Fake.Repository(null, product));

        IResult result = await ProductsEndpoints.RestockAsync(Guid.CreateVersion7(), new RestockProductRequest(12), handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{Guid.CreateVersion7()}/restock", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:not-found", response.Text("type"));
    }

    // The endpoint does not check the units itself: the domain throws and the
    // global handler answers with the rule's own problem type.
    [Fact]
    public async Task RestockAsync_WhenTheUnitsAreNotPositive_LetsTheDomainExceptionThrough()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 0);
        RestockProductHandler handler = new(new Fake.Repository(null, product));

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => ProductsEndpoints.RestockAsync(product.Id, new RestockProductRequest(0), handler, CancellationToken.None));

        Assert.Equal(Product.ErrorCodes.RestockNotPositive, error.Code);
    }

    private static readonly byte[] APng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static readonly byte[] APdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A, 0x0A];

    [Fact]
    public async Task UploadImageAsync_WhenTheImageIsValid_RespondsWithTheNewAddress()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);

        HttpResponseSnapshot response = await Upload(product, product.Id, APng);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.StartsWith($"https://images.example.com/products/{product.Id}/", response.Text("imageUrl"), StringComparison.Ordinal);
    }

    // Another venue's id looks exactly like this: the repository, scoped by the
    // venue filter, finds nothing, and nothing is what the answer says.
    [Fact]
    public async Task UploadImageAsync_WhenTheProductIsNotInThisVenue_RespondsWithNotFound()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);

        HttpResponseSnapshot response = await Upload(product, Guid.CreateVersion7(), APng);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:not-found", response.Text("type"));
    }

    [Fact]
    public async Task UploadImageAsync_WhenTheImageIsTooLarge_RespondsWithPayloadTooLarge()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);

        HttpResponseSnapshot response = await Upload(product, product.Id, APng, declaredLength: UploadProductImageHandler.MaxImageBytes + 1);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:image-too-large", response.Text("type"));
    }

    [Fact]
    public async Task UploadImageAsync_WhenTheBytesAreNotAnImageWeShow_RespondsWithUnsupportedMediaType()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);

        HttpResponseSnapshot response = await Upload(product, product.Id, APdf);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:image-format-unsupported", response.Text("type"));
    }

    // A multipart body with no file in it is malformed input, not a missing
    // product: it gets its own answer so the screen can say "choose a photo".
    [Fact]
    public async Task UploadImageAsync_WhenNoFileWasSent_RespondsWithBadRequest()
    {
        Product product = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20);
        UploadProductImageHandler handler = new(new Fake.Repository(null, product), new Fake.Images());

        IResult result = await ProductsEndpoints.UploadImageAsync(product.Id, null, handler, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{product.Id}/image", HttpMethods.Put);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:image-required", response.Text("type"));
    }

    private static async Task<HttpResponseSnapshot> Upload(Product stored, Guid productId, byte[] bytes, long? declaredLength = null)
    {
        UploadProductImageHandler handler = new(new Fake.Repository(null, stored), new Fake.Images());
        FormFile file = new(new MemoryStream(bytes), 0, declaredLength ?? bytes.Length, "image", "photo.png");

        IResult result = await ProductsEndpoints.UploadImageAsync(productId, file, handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, $"{Path}/{productId}/image", HttpMethods.Put);
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

        public sealed class Repository(string? taken, Product? stored = null) : IProductRepository
        {
            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(string.Equals(name, taken, StringComparison.OrdinalIgnoreCase));

            public Task AddAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored?.Id == id ? stored : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public sealed class Images : IImageStore
        {
            public Task<string> SaveAsync(string name, Stream content, string contentType, CancellationToken cancellationToken) =>
                Task.FromResult($"https://images.example.com/{name}");
        }

        public sealed class Queries(ProductListItem[] stored) : IProductQueries
        {
            public Task<IReadOnlyList<ProductListItem>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ProductListItem>>(stored);

            public Task<IReadOnlyList<MenuItem>> ListForMenuAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException("The administration listing never reads the customer's menu.");
        }
    }
}
