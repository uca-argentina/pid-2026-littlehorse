using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

public class UpdateProductHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly Guid TheCategory = Guid.CreateVersion7();

    private static readonly Guid AnotherCategory = Guid.CreateVersion7();

    private static Product AGinTonic() =>
        Product.Create(TheVenue, "Gin Tonic", "Gin, tonic and a slice of lime.", null, 4500m, 20, TheCategory);

    [Fact]
    public async Task HandleAsync_WhenValid_UpdatesAndSaves()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product);

        Result<ProductSummary> result = await new UpdateProductHandler(products, new FakeCategories()).HandleAsync(
            new UpdateProductCommand(product.Id, "Fernet con Coca", "Medida doble.", 3800m, AnotherCategory),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Fernet con Coca", result.Value.Name);
        Assert.Equal(3800m, result.Value.Price);
        Assert.Equal(AnotherCategory, result.Value.CategoryId);
        Assert.True(products.Saved);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductDoesNotExistInThisVenue_FailsWithoutSaving()
    {
        Fake.Products products = new(AGinTonic());

        Result<ProductSummary> result = await new UpdateProductHandler(products, new FakeCategories()).HandleAsync(
            new UpdateProductCommand(Guid.CreateVersion7(), "Fernet con Coca", null, 3800m, TheCategory),
            CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.False(products.Saved);
    }

    // Keeping the name it already had is not a collision with itself.
    [Fact]
    public async Task HandleAsync_WhenTheNameDoesNotChange_DoesNotCheckForACollision()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product) { NameExistsAnswer = true };

        Result<ProductSummary> result = await new UpdateProductHandler(products, new FakeCategories()).HandleAsync(
            new UpdateProductCommand(product.Id, "Gin Tonic", null, 5000m, TheCategory),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // Matches however it was capitalised, same as at creation: the customer
    // reads "gin tonic" and "Gin Tonic" as the one product.
    [Fact]
    public async Task HandleAsync_WhenTheNameOnlyChangesCase_DoesNotCheckForACollision()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product) { NameExistsAnswer = true };

        Result<ProductSummary> result = await new UpdateProductHandler(products, new FakeCategories()).HandleAsync(
            new UpdateProductCommand(product.Id, "GIN TONIC", null, 5000m, TheCategory),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNewNameIsAlreadyTakenByAnotherProduct_FailsWithoutSaving()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product) { NameExistsAnswer = true };

        Result<ProductSummary> result = await new UpdateProductHandler(products, new FakeCategories()).HandleAsync(
            new UpdateProductCommand(product.Id, "Fernet con Coca", null, 3800m, TheCategory),
            CancellationToken.None);

        Assert.Equal(CreateProductHandler.NameTaken, result.Error);
        Assert.False(products.Saved);
        Assert.Equal("Gin Tonic", product.Name);
    }

    // A broken invariant (blank name, price at zero) is not caught here: the
    // domain throws and the global handler answers, same as at creation.
    [Fact]
    public async Task HandleAsync_WhenThePriceIsNotPositive_LetsTheDomainExceptionThrough()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product);

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => new UpdateProductHandler(products, new FakeCategories())
            .HandleAsync(new UpdateProductCommand(product.Id, "Gin Tonic", null, 0m, TheCategory), CancellationToken.None));

        Assert.Equal(Product.ErrorCodes.PriceNotPositive, error.Code);
        Assert.False(products.Saved);
    }

    // Same answer as at creation: a category of another venue is not found.
    [Fact]
    public async Task HandleAsync_WhenTheCategoryIsNotOneOfThisVenue_FailsWithoutSaving()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product);

        Result<ProductSummary> result = await new UpdateProductHandler(products, new FakeCategories(knowsEveryId: false)).HandleAsync(
            new UpdateProductCommand(product.Id, "Fernet con Coca", null, 3800m, AnotherCategory),
            CancellationToken.None);

        Assert.Equal(ProductErrors.CategoryNotFound, result.Error);
        Assert.False(products.Saved);
        Assert.Equal(TheCategory, product.CategoryId);
    }

    private static class Fake
    {
        public sealed class Products(Product stored) : IProductRepository
        {
            public Task<bool> SaveStockAdjustmentAsync(Product product, int change, CancellationToken cancellationToken) =>
                Task.FromResult(true);

            public bool Saved { get; private set; }

            public bool NameExistsAnswer { get; init; }

            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(NameExistsAnswer);

            public Task AddAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored.Id == id ? stored : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken)
            {
                Saved = true;

                return Task.CompletedTask;
            }
        }
    }
}
