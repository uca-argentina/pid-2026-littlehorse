using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Tests.Staff;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Nights;

/// <summary>US-37: loading one bar's stock for a night. A bar is a KDS of the night's crew.</summary>
public class AdjustNightStockHandlerTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    private static readonly StaffUser MainBar = StaffUser.Create(AVenue, "main-bar", "hash", StaffRole.Kds);
    private static readonly StaffUser VipBar = StaffUser.Create(AVenue, "vip-bar", "hash", StaffRole.Kds);
    private static readonly StaffUser Till = StaffUser.Create(AVenue, "till-1", "hash", StaffRole.Cashier);

    private static Night ANightStartingAt(DateTimeOffset startsAt) =>
        Night.Create(AVenue, "Saturday", startsAt, startsAt.AddHours(7), [MainBar, Till]);

    private static Product AGinTonic() => Product.Create(AVenue, "Gin Tonic", null, null, 9500m, 0, Guid.CreateVersion7());

    private static AdjustNightStockHandler Handler(NightsInMemory nights, Product product, Fake.Shelves shelves, DateTimeOffset now) =>
        new(nights, new Fake.Products(product), new StaffUsersInMemory(MainBar, VipBar, Till), shelves, new Fake.Sheets(), new Fake.FixedClock(now));

    private static Task<Result<NightStockSheet>> Adjust(
        Night? night,
        Product product,
        Fake.Shelves shelves,
        DateTimeOffset now,
        int change = 12,
        Guid? barId = null,
        Guid? productId = null,
        Night? earlier = null)
    {
        NightsInMemory nights = new([.. new[] { earlier, night }.OfType<Night>()]);

        return Handler(nights, product, shelves, now).HandleAsync(
            new AdjustNightStockCommand(night?.Id ?? Guid.CreateVersion7(), barId ?? MainBar.Id, productId ?? product.Id, change),
            CancellationToken.None);
    }

    [Fact]
    public async Task HandleAsync_WhenNightIsUpcoming_SavesTheAdjustmentForThatBar()
    {
        Night night = ANightStartingAt(Opening);
        Product ginTonic = AGinTonic();
        Fake.Shelves shelves = new();

        Result<NightStockSheet> result = await Adjust(night, ginTonic, shelves, Opening.AddHours(-5));

        Assert.True(result.IsSuccess);
        Assert.Equal(night.Id, result.Value.NightId);
        Assert.Equal(night.Id, shelves.Saved?.NightId);
        Assert.Equal(MainBar.Id, shelves.Saved?.KdsId);
        Assert.Equal(ginTonic.Id, shelves.Saved?.ProductId);
        Assert.Equal(12, shelves.Saved?.Change);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNightDoesNotExistInThisVenue_FailsWithNotFound()
    {
        Fake.Shelves shelves = new();

        Result<NightStockSheet> result = await Adjust(null, AGinTonic(), shelves, Opening);

        Assert.Equal(NightErrors.NotFound, result.Error);
        Assert.Null(shelves.Saved);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductDoesNotExistInThisVenue_FailsWithProductNotFound()
    {
        Fake.Shelves shelves = new();

        Result<NightStockSheet> result = await Adjust(ANightStartingAt(Opening), AGinTonic(), shelves, Opening, productId: Guid.CreateVersion7());

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.Null(shelves.Saved);
    }

    [Fact]
    public async Task HandleAsync_WhenTheBarDoesNotExistInThisVenue_FailsWithBarNotFound()
    {
        Fake.Shelves shelves = new();

        Result<NightStockSheet> result = await Adjust(ANightStartingAt(Opening), AGinTonic(), shelves, Opening, barId: Guid.CreateVersion7());

        Assert.Equal(NightErrors.BarNotFound, result.Error);
        Assert.Null(shelves.Saved);
    }

    // Taken out of the crew while the screen was open: something that happens.
    [Fact]
    public async Task HandleAsync_WhenTheBarDoesNotWorkTheNight_FailsWithBarNotInNight()
    {
        Fake.Shelves shelves = new();

        Result<NightStockSheet> result = await Adjust(ANightStartingAt(Opening), AGinTonic(), shelves, Opening, barId: VipBar.Id);

        Assert.Equal(NightErrors.BarNotInNight, result.Error);
        Assert.Null(shelves.Saved);
    }

    // Criterion 3: a finished night is only looked at.
    [Fact]
    public async Task HandleAsync_WhenTheNightIsOver_FailsWithStockClosedWithoutSaving()
    {
        Night night = ANightStartingAt(Opening);
        Fake.Shelves shelves = new();

        Result<NightStockSheet> result = await Adjust(night, AGinTonic(), shelves, night.EndsAt);

        Assert.Equal(NightErrors.StockClosed, result.Error);
        Assert.Null(shelves.Saved);
    }

    // Saturday starts from what Friday leaves, and Friday is still selling.
    [Fact]
    public async Task HandleAsync_WhenAnEarlierNightHasNotEnded_FailsWithEarlierNightOpen()
    {
        Night friday = ANightStartingAt(Opening.AddDays(-1));
        Fake.Shelves shelves = new();

        Result<NightStockSheet> result = await Adjust(ANightStartingAt(Opening), AGinTonic(), shelves, friday.StartsAt.AddHours(1), earlier: friday);

        Assert.Equal(NightErrors.EarlierNightOpen, result.Error);
        Assert.Null(shelves.Saved);
    }

    [Fact]
    public async Task HandleAsync_WhenTheEarlierNightAlreadyEnded_SavesTheAdjustment()
    {
        Night friday = ANightStartingAt(Opening.AddDays(-1));
        Fake.Shelves shelves = new();

        Result<NightStockSheet> result = await Adjust(ANightStartingAt(Opening), AGinTonic(), shelves, friday.EndsAt, earlier: friday);

        Assert.True(result.IsSuccess);
    }

    // The bar has less than the change takes away, sold since the screen opened.
    [Fact]
    public async Task HandleAsync_WhenTheBarHasLessThanItTakesAway_FailsWithStockMoved()
    {
        Fake.Shelves shelves = new(applies: false);

        Result<NightStockSheet> result = await Adjust(ANightStartingAt(Opening), AGinTonic(), shelves, Opening, change: -5);

        Assert.Equal(ProductErrors.StockMoved, result.Error);
    }

    // A malformed request, not something that happened meanwhile.
    [Fact]
    public async Task HandleAsync_WhenTheChangeIsZero_LetsTheDomainExceptionThrowWithoutSaving()
    {
        Fake.Shelves shelves = new();

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => Adjust(ANightStartingAt(Opening), AGinTonic(), shelves, Opening, change: 0));

        Assert.Equal(Night.ErrorCodes.StockChangeZero, error.Code);
        Assert.Null(shelves.Saved);
    }

    private static class Fake
    {
        public sealed class FixedClock(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        }

        public sealed class Products(Product stored) : IProductRepository
        {
            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) => Task.FromResult(false);

            public Task AddAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored.Id == id ? stored : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        /// <summary>The bars' stock. <c>applies: false</c> is a bar left with less than the change takes away.</summary>
        public sealed class Shelves(bool applies = true) : INightStockRepository
        {
            public NightStockAdjustment? Saved { get; private set; }

            public Task<bool> SaveAdjustmentAsync(NightStockAdjustment adjustment, CancellationToken cancellationToken)
            {
                if (applies) Saved = adjustment;

                return Task.FromResult(applies);
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
