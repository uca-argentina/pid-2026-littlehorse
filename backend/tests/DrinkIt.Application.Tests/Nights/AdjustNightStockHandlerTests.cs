using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Nights;

public class AdjustNightStockHandlerTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    private static Night ANightStartingAt(DateTimeOffset startsAt) => Night.Create(
        AVenue,
        "Saturday",
        startsAt,
        startsAt.AddHours(7),
        [StaffUser.Create(AVenue, "main-bar", "hash", StaffRole.Kds), StaffUser.Create(AVenue, "till-1", "hash", StaffRole.Cashier)]);

    private static Product AGinTonicWith(int stock) =>
        Product.Create(AVenue, "Gin Tonic", null, null, 9500m, stock, Guid.CreateVersion7());

    private static AdjustNightStockHandler Handler(NightsInMemory nights, Fake.Products products, DateTimeOffset now) =>
        new(nights, products, new Fake.Sheets(), new Fake.FixedClock(now));

    [Fact]
    public async Task HandleAsync_WhenNightIsUpcoming_SavesTheAdjustmentWithTheProductsStock()
    {
        Night night = ANightStartingAt(Opening);
        Product ginTonic = AGinTonicWith(20);
        Fake.Products products = new(ginTonic);

        Result<NightStockSheet> result = await Handler(new NightsInMemory(night), products, Opening.AddHours(-5))
            .HandleAsync(new AdjustNightStockCommand(night.Id, ginTonic.Id, 12), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(night.Id, result.Value.NightId);
        Assert.Equal(night.Id, products.Saved?.NightId);
        Assert.Equal(12, products.Saved?.Change);
        Assert.Equal(32, ginTonic.Stock);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNightDoesNotExistInThisVenue_FailsWithNotFound()
    {
        Product ginTonic = AGinTonicWith(20);
        Fake.Products products = new(ginTonic);

        Result<NightStockSheet> result = await Handler(new NightsInMemory(), products, Opening)
            .HandleAsync(new AdjustNightStockCommand(Guid.CreateVersion7(), ginTonic.Id, 12), CancellationToken.None);

        Assert.Equal(NightErrors.NotFound, result.Error);
        Assert.Null(products.Saved);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductDoesNotExistInThisVenue_FailsWithProductNotFound()
    {
        Night night = ANightStartingAt(Opening);
        Fake.Products products = new(AGinTonicWith(20));

        Result<NightStockSheet> result = await Handler(new NightsInMemory(night), products, Opening)
            .HandleAsync(new AdjustNightStockCommand(night.Id, Guid.CreateVersion7(), 12), CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.Null(products.Saved);
    }

    // Criterion 3: a finished night is only looked at. A screen opened before
    // the end can still send one, and that is something that happens.
    [Fact]
    public async Task HandleAsync_WhenTheNightIsOver_FailsWithStockClosedWithoutSaving()
    {
        Night night = ANightStartingAt(Opening);
        Product ginTonic = AGinTonicWith(20);
        Fake.Products products = new(ginTonic);

        Result<NightStockSheet> result = await Handler(new NightsInMemory(night), products, night.EndsAt)
            .HandleAsync(new AdjustNightStockCommand(night.Id, ginTonic.Id, 12), CancellationToken.None);

        Assert.Equal(NightErrors.StockClosed, result.Error);
        Assert.Null(products.Saved);
        Assert.Equal(20, ginTonic.Stock);
    }

    // Saturday starts from what Friday leaves, and Friday is still selling:
    // loading Saturday now would put those units on Friday's shelf.
    [Fact]
    public async Task HandleAsync_WhenAnEarlierNightHasNotEnded_FailsWithEarlierNightOpen()
    {
        Night friday = ANightStartingAt(Opening.AddDays(-1));
        Night saturday = ANightStartingAt(Opening);
        Product ginTonic = AGinTonicWith(20);
        Fake.Products products = new(ginTonic);

        Result<NightStockSheet> result = await Handler(new NightsInMemory(friday, saturday), products, friday.StartsAt.AddHours(1))
            .HandleAsync(new AdjustNightStockCommand(saturday.Id, ginTonic.Id, 12), CancellationToken.None);

        Assert.Equal(NightErrors.EarlierNightOpen, result.Error);
        Assert.Null(products.Saved);
    }

    [Fact]
    public async Task HandleAsync_WhenTheEarlierNightAlreadyEnded_SavesTheAdjustment()
    {
        Night friday = ANightStartingAt(Opening.AddDays(-1));
        Night saturday = ANightStartingAt(Opening);
        Product ginTonic = AGinTonicWith(20);
        Fake.Products products = new(ginTonic);

        Result<NightStockSheet> result = await Handler(new NightsInMemory(friday, saturday), products, friday.EndsAt)
            .HandleAsync(new AdjustNightStockCommand(saturday.Id, ginTonic.Id, 12), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // The screen worked the change out from what it showed when it opened.
    [Fact]
    public async Task HandleAsync_WhenWhatIsLeftIsLessThanItTakesAway_FailsWithStockMovedWithoutSaving()
    {
        Night night = ANightStartingAt(Opening);
        Product ginTonic = AGinTonicWith(2);
        Fake.Products products = new(ginTonic);

        Result<NightStockSheet> result = await Handler(new NightsInMemory(night), products, Opening)
            .HandleAsync(new AdjustNightStockCommand(night.Id, ginTonic.Id, -5), CancellationToken.None);

        Assert.Equal(ProductErrors.StockMoved, result.Error);
        Assert.Null(products.Saved);
    }

    // A sale landed between the read and the write.
    [Fact]
    public async Task HandleAsync_WhenSalesMeanwhileLeftTooLittle_FailsWithStockMoved()
    {
        Night night = ANightStartingAt(Opening);
        Product ginTonic = AGinTonicWith(5);
        Fake.Products products = new(ginTonic, adjustmentApplies: false);

        Result<NightStockSheet> result = await Handler(new NightsInMemory(night), products, Opening)
            .HandleAsync(new AdjustNightStockCommand(night.Id, ginTonic.Id, -5), CancellationToken.None);

        Assert.Equal(ProductErrors.StockMoved, result.Error);
    }

    // A malformed request, not something that happened meanwhile.
    [Fact]
    public async Task HandleAsync_WhenTheChangeIsZero_LetsTheDomainExceptionThrowWithoutSaving()
    {
        Night night = ANightStartingAt(Opening);
        Product ginTonic = AGinTonicWith(5);
        Fake.Products products = new(ginTonic);

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => Handler(new NightsInMemory(night), products, Opening)
            .HandleAsync(new AdjustNightStockCommand(night.Id, ginTonic.Id, 0), CancellationToken.None));

        Assert.Equal(Product.ErrorCodes.StockChangeZero, error.Code);
        Assert.Null(products.Saved);
    }

    private static class Fake
    {
        public sealed class FixedClock(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        }

        public sealed class Products(Product stored, bool adjustmentApplies = true) : IProductRepository
        {
            public NightStockAdjustment? Saved { get; private set; }

            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) => Task.FromResult(false);

            public Task AddAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored.Id == id ? stored : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<bool> SaveStockAdjustmentAsync(Product product, NightStockAdjustment adjustment, CancellationToken cancellationToken)
            {
                if (adjustmentApplies) Saved = adjustment;

                return Task.FromResult(adjustmentApplies);
            }
        }

        /// <summary>What the sheet says is the query's business, tested against the database.</summary>
        public sealed class Sheets : INightStockQueries
        {
            public Task<NightStockSheet?> GetAsync(Guid nightId, CancellationToken cancellationToken) =>
                Task.FromResult<NightStockSheet?>(new NightStockSheet(nightId, []));
        }
    }
}
