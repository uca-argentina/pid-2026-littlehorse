using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

public class DeactivateProductHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static Product AGinTonic() => Product.Create(TheVenue, "Gin Tonic", null, null, 4500m, 20);

    [Fact]
    public async Task HandleAsync_WhenTheProductExists_DeactivatesAndSaves()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product);

        Result<ProductSummary> result = await new DeactivateProductHandler(products)
            .HandleAsync(product.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);
        Assert.False(product.IsActive);
        Assert.True(products.Saved);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductDoesNotExistInThisVenue_FailsWithoutSaving()
    {
        Fake.Products products = new(AGinTonic());

        Result<ProductSummary> result = await new DeactivateProductHandler(products)
            .HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.False(products.Saved);
    }

    private static class Fake
    {
        public sealed class Products(Product stored) : IProductRepository
        {
            public Task<bool> SaveStockAdjustmentAsync(Product product, int change, CancellationToken cancellationToken) =>
                Task.FromResult(true);

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
