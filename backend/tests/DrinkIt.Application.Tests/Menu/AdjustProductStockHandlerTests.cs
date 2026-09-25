using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

public class AdjustProductStockHandlerTests
{
    private static Product AGinTonicWith(int stock) =>
        Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, stock, ProductCategory.Drink);

    // Loaded 200 when it was 20.
    [Fact]
    public async Task HandleAsync_WhenTheProductExists_SavesTheChangeAndNotANewTotal()
    {
        Product product = AGinTonicWith(200);
        Fake.Products products = new(product);

        Result<ProductSummary> result = await new AdjustProductStockHandler(products)
            .HandleAsync(new AdjustProductStockCommand(product.Id, -180), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value.Stock);
        Assert.Equal(-180, products.SavedChange);
        Assert.False(products.SavedEverything);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductDoesNotExistInThisVenue_FailsWithoutSaving()
    {
        Fake.Products products = new(AGinTonicWith(0));

        Result<ProductSummary> result = await new AdjustProductStockHandler(products)
            .HandleAsync(new AdjustProductStockCommand(Guid.CreateVersion7(), 12), CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.Null(products.SavedChange);
    }

    // The screen worked the change out from the stock it showed when it opened;
    // sales since then left less than it takes away. Expected on a busy night,
    // so a result and not an exception, and the same answer as when the sale
    // lands a millisecond later, between the read and the write.
    [Fact]
    public async Task HandleAsync_WhenWhatIsLeftIsLessThanItTakesAway_FailsWithStockMovedWithoutSaving()
    {
        Product product = AGinTonicWith(2);
        Fake.Products products = new(product);

        Result<ProductSummary> result = await new AdjustProductStockHandler(products)
            .HandleAsync(new AdjustProductStockCommand(product.Id, -5), CancellationToken.None);

        Assert.Equal(ProductErrors.StockMoved, result.Error);
        Assert.Null(products.SavedChange);
        Assert.Equal(2, product.Stock);
    }

    // A change of nothing is a malformed request, not something that happened
    // meanwhile: the domain throws and the global handler answers 400.
    [Fact]
    public async Task HandleAsync_WhenTheChangeIsZero_LetsTheDomainExceptionThrowWithoutSaving()
    {
        Product product = AGinTonicWith(5);
        Fake.Products products = new(product);

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => new AdjustProductStockHandler(products)
            .HandleAsync(new AdjustProductStockCommand(product.Id, 0), CancellationToken.None));

        Assert.Equal(Product.ErrorCodes.StockChangeZero, error.Code);
        Assert.Null(products.SavedChange);
    }

    // The bar sold some between the screen opening and this arriving, and what
    // is left is less than the change takes away. Nothing is written.
    [Fact]
    public async Task HandleAsync_WhenSalesMeanwhileLeftTooLittle_FailsWithStockMoved()
    {
        Product product = AGinTonicWith(5);
        Fake.Products products = new(product, adjustmentApplies: false);

        Result<ProductSummary> result = await new AdjustProductStockHandler(products)
            .HandleAsync(new AdjustProductStockCommand(product.Id, -5), CancellationToken.None);

        Assert.Equal(ProductErrors.StockMoved, result.Error);
    }

    private static class Fake
    {
        public sealed class Products(Product stored, bool adjustmentApplies = true) : IProductRepository
        {
            public int? SavedChange { get; private set; }

            public bool SavedEverything { get; private set; }

            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(false);

            public Task AddAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored.Id == id ? stored : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken)
            {
                SavedEverything = true;

                return Task.CompletedTask;
            }

            public Task<bool> SaveStockAdjustmentAsync(Product product, int change, CancellationToken cancellationToken)
            {
                SavedChange = change;

                return Task.FromResult(adjustmentApplies);
            }
        }
    }
}
