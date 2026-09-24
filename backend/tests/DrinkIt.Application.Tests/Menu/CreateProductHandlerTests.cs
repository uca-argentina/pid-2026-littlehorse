using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

public class CreateProductHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static CreateProductCommand AGinTonic(string name = "Gin Tonic") =>
        new(name, "Gin, tonic and a slice of lime.", "https://images.example.com/gin-tonic.jpg", 4500m, 20);

    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_AddsTheProductToTheVenueOfTheSignedInAdministrator()
    {
        Fake.Products products = new();

        Result<ProductSummary> result = await HandlerOver(products).HandleAsync(AGinTonic(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TheVenue, products.Added!.VenueId);
        Assert.Equal("Gin Tonic", products.Added.Name);
        Assert.Equal("Gin, tonic and a slice of lime.", products.Added.Description);
        Assert.Equal("https://images.example.com/gin-tonic.jpg", products.Added.ImageUrl);
        Assert.Equal(4500m, products.Added.Price);
        Assert.Equal(20, products.Added.Stock);
    }

    // US-06, criterion 1: it shows up on the menu right after saving, so it
    // cannot be born switched off or deactivated.
    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_LeavesTheProductOnTheMenu()
    {
        Fake.Products products = new();

        await HandlerOver(products).HandleAsync(AGinTonic(), CancellationToken.None);

        Assert.True(products.Added!.IsAvailable);
        Assert.True(products.Added.IsActive);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNameIsAlreadyUsedInThisVenue_Fails()
    {
        Fake.Products products = new(taken: "Gin Tonic");

        Result<ProductSummary> result = await HandlerOver(products).HandleAsync(AGinTonic(), CancellationToken.None);

        Assert.Equal(CreateProductHandler.NameTaken, result.Error);
        Assert.Null(products.Added);
    }

    // Two products that differ only in casing or padding are the same product
    // to the customer. The check runs on the trimmed name, and the repository
    // ignores case, so "gin tonic" cannot slip past a "Gin Tonic".
    [Theory]
    [InlineData("gin tonic")]
    [InlineData("  Gin Tonic  ")]
    [InlineData("GIN TONIC")]
    public async Task HandleAsync_WhenTheTakenNameIsTypedWithOtherCasingOrPadding_StillFails(string name)
    {
        Fake.Products products = new(taken: "Gin Tonic");

        Result<ProductSummary> result = await HandlerOver(products).HandleAsync(AGinTonic(name), CancellationToken.None);

        Assert.Equal(CreateProductHandler.NameTaken, result.Error);
    }

    // A broken invariant is not an expected outcome: the domain throws, the API
    // turns it into a 400, and the handler does not restate the rules.
    [Fact]
    public async Task HandleAsync_WhenTheDomainRejectsTheData_ThrowsWithoutCreatingAnything()
    {
        Fake.Products products = new();
        CreateProductCommand priceless = AGinTonic() with { Price = 0 };

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => HandlerOver(products).HandleAsync(priceless, CancellationToken.None));

        Assert.Equal(Product.ErrorCodes.PriceNotPositive, error.Code);
        Assert.Null(products.Added);
    }

    // What the screen puts in the list right after saving.
    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_ReturnsWhatTheListingShows()
    {
        Fake.Products products = new();

        Result<ProductSummary> result = await HandlerOver(products).HandleAsync(
            AGinTonic("  Gin Tonic  ") with { Stock = 0 },
            CancellationToken.None);

        Assert.Equal(products.Added!.Id, result.Value.Id);
        Assert.Equal("Gin Tonic", result.Value.Name);
        Assert.Equal("Gin, tonic and a slice of lime.", result.Value.Description);
        Assert.Equal("https://images.example.com/gin-tonic.jpg", result.Value.ImageUrl);
        Assert.Equal(4500m, result.Value.Price);
        Assert.Equal(0, result.Value.Stock);
        Assert.True(result.Value.IsSoldOut);
        Assert.True(result.Value.IsAvailable);
        Assert.True(result.Value.IsActive);
    }

    private static CreateProductHandler HandlerOver(Fake.Products products) =>
        new(products, new Fake.CurrentVenue());

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id => TheVenue;
        }

        public sealed class Products(string? taken = null) : IProductRepository
        {
            public Task<bool> SaveStockAdjustmentAsync(Product product, int change, CancellationToken cancellationToken) =>
                Task.FromResult(true);

            public Product? Added { get; private set; }

            // Ignores case like the real one: the database collation does the
            // same, and the contract of NameExistsAsync says so.
            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(string.Equals(name, taken, StringComparison.OrdinalIgnoreCase));

            public Task AddAsync(Product product, CancellationToken cancellationToken)
            {
                Added = product;

                return Task.CompletedTask;
            }

            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult<Product?>(null);

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
