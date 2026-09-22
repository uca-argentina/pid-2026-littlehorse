using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

public class RestockProductHandlerTests
{
    private static Product ASoldOutGinTonic() =>
        Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 0);

    [Fact]
    public async Task HandleAsync_WhenTheProductExists_AddsTheUnitsAndSaves()
    {
        Product product = ASoldOutGinTonic();
        Fake.Products products = new(product);

        Result<ProductSummary> result = await new RestockProductHandler(products)
            .HandleAsync(new RestockProductCommand(product.Id, 12), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value.Stock);
        Assert.False(result.Value.IsSoldOut);
        Assert.Equal(12, product.Stock);
        Assert.True(products.Saved);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductDoesNotExistInThisVenue_FailsWithoutSaving()
    {
        Fake.Products products = new(ASoldOutGinTonic());

        Result<ProductSummary> result = await new RestockProductHandler(products)
            .HandleAsync(new RestockProductCommand(Guid.CreateVersion7(), 12), CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.False(products.Saved);
    }

    // A broken invariant is not caught here: the domain throws and the global
    // handler answers, same as when a product is created or corrected.
    [Fact]
    public async Task HandleAsync_WhenTheUnitsAreNotPositive_LetsTheDomainExceptionThrowWithoutSaving()
    {
        Product product = ASoldOutGinTonic();
        Fake.Products products = new(product);

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => new RestockProductHandler(products)
            .HandleAsync(new RestockProductCommand(product.Id, 0), CancellationToken.None));

        Assert.Equal(Product.ErrorCodes.RestockNotPositive, error.Code);
        Assert.False(products.Saved);
    }

    private static class Fake
    {
        public sealed class Products(Product stored) : IProductRepository
        {
            public bool Saved { get; private set; }

            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(false);

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
