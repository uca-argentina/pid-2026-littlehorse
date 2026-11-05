using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

public class ComboHandlersTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly Guid TheCategory = Guid.CreateVersion7();

    private static readonly Product Fernet = Product.Create(TheVenue, "Fernet", null, null, 4000m, 20, TheCategory);

    private static readonly Product Ice = Product.Create(TheVenue, "Hielo", null, null, 500m, 20, TheCategory);

    private readonly Shelf _shelf = new(Fernet, Ice);

    private static CreateComboCommand ACombo(params ComboPart[] parts) => new(
        "Combo Fernet",
        null,
        null,
        9000m,
        TheCategory,
        parts.Length == 0 ? [new ComboPart(Fernet.Id, 2), new ComboPart(Ice.Id, 1)] : parts);

    private CreateComboHandler ACreator(Repository? products = null) =>
        new(products ?? new Repository(), new FakeCategories(), _shelf, new TheVenueIsFixed());

    // Criterion 1.
    [Fact]
    public async Task HandleAsync_WhenItIsValid_AddsTheComboWithItsComponentsToTheVenue()
    {
        Repository products = new();

        Result<ProductSummary> result = await ACreator(products).HandleAsync(ACombo(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TheVenue, products.Added!.VenueId);
        Assert.True(result.Value.IsCombo);
        Assert.Equal(2, result.Value.Components.Count);
        Assert.False(result.Value.IsSoldOut);
    }

    [Fact]
    public async Task HandleAsync_WhenAComponentIsNotOfThisVenue_FailsWithoutCreatingIt()
    {
        Repository products = new();

        Result<ProductSummary> result = await ACreator(products).HandleAsync(
            ACombo(new ComboPart(Guid.CreateVersion7(), 2)),
            CancellationToken.None);

        Assert.Equal(ProductErrors.ComboComponentNotFound, result.Error);
        Assert.Null(products.Added);
    }

    [Fact]
    public async Task HandleAsync_WhenAComponentWasTakenOffTheMenu_FailsWithoutCreatingIt()
    {
        Product gone = Product.Create(TheVenue, "Gone", null, null, 100m, 1, TheCategory);
        gone.Deactivate();
        _shelf.Holding.Add(gone);

        Result<ProductSummary> result = await ACreator().HandleAsync(ACombo(new ComboPart(gone.Id, 2)), CancellationToken.None);

        Assert.Equal(ProductErrors.ComboComponentNotFound, result.Error);
    }

    // Criterion 2.
    [Fact]
    public async Task HandleAsync_WhenAComponentIsACombo_FailsWithoutCreatingIt()
    {
        Product other = Product.CreateCombo(TheVenue, "Other", null, null, 100m, TheCategory, [(Fernet.Id, 2)]);
        _shelf.Holding.Add(other);

        Result<ProductSummary> result = await ACreator().HandleAsync(ACombo(new ComboPart(other.Id, 2)), CancellationToken.None);

        Assert.Equal(ProductErrors.ComboInsideCombo, result.Error);
    }

    // Criterion 2: the rule itself is the domain's, and the API turns it into a 400.
    [Fact]
    public async Task HandleAsync_WhenItAddsUpToOneUnit_ThrowsTheDomainsRule()
    {
        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => ACreator().HandleAsync(ACombo(new ComboPart(Fernet.Id, 1)), CancellationToken.None));

        Assert.Equal(Product.ErrorCodes.ComboTooFewUnits, error.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNameIsTaken_FailsWithTheProductsOwnAnswer()
    {
        Result<ProductSummary> result = await ACreator(new Repository(taken: "Combo Fernet")).HandleAsync(ACombo(), CancellationToken.None);

        Assert.Equal(CreateProductHandler.NameTaken, result.Error);
    }

    [Fact]
    public async Task Replace_WhenTheComboExists_SavesTheNewComponents()
    {
        Product combo = Product.CreateCombo(TheVenue, "Combo", null, null, 100m, TheCategory, [(Fernet.Id, 2)]);
        Repository products = new(stored: combo);

        Result<ProductSummary> result = await new ReplaceComboComponentsHandler(products, _shelf).HandleAsync(
            new ReplaceComboComponentsCommand(combo.Id, [new ComboPart(Ice.Id, 3)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(products.Saved);
        Assert.Equal(Ice.Id, Assert.Single(combo.Components).ProductId);
    }

    [Fact]
    public async Task Replace_WhenTheComboIsNotOfThisVenue_FailsAsNotFound()
    {
        Result<ProductSummary> result = await new ReplaceComboComponentsHandler(new Repository(), _shelf).HandleAsync(
            new ReplaceComboComponentsCommand(Guid.CreateVersion7(), [new ComboPart(Ice.Id, 3)]),
            CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task AdjustStock_WhenItIsACombo_FailsWithItsOwnAnswer()
    {
        Product combo = Product.CreateCombo(TheVenue, "Combo", null, null, 100m, TheCategory, [(Fernet.Id, 2)]);

        Result<ProductSummary> result = await new AdjustProductStockHandler(new Repository(stored: combo), TimeProvider.System).HandleAsync(
            new AdjustProductStockCommand(combo.Id, -1),
            CancellationToken.None);

        Assert.Equal(ProductErrors.ComboHasNoStock, result.Error);
    }

    private sealed class TheVenueIsFixed : ICurrentVenue
    {
        public Guid Id => TheVenue;
    }

    private sealed class Shelf(params Product[] products) : IProductsForOrdering
    {
        public List<Product> Holding { get; } = [.. products];

        public Task<IReadOnlyList<Product>> GetForOrderingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Product>>([.. Holding.Where(product => ids.Contains(product.Id))]);
    }

    private sealed class Repository(string? taken = null, Product? stored = null) : IProductRepository
    {
        public Product? Added { get; private set; }

        public bool Saved { get; private set; }

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(name, taken, StringComparison.OrdinalIgnoreCase));

        public Task AddAsync(Product product, CancellationToken cancellationToken)
        {
            Added = product;

            return Task.CompletedTask;
        }

        public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(stored?.Id == id ? stored : null);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saved = true;

            return Task.CompletedTask;
        }

        public Task<bool> SaveStockAdjustmentAsync(Product product, int change, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
