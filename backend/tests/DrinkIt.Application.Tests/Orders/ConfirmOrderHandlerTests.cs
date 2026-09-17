using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Orders;

public class ConfirmOrderHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Tonight = new(2026, 9, 17, 2, 30, 0, TimeSpan.Zero);

    // xUnit builds one of these per test, so every test gets its own menu and
    // its own orders: nothing one of them sells is missing from the next.
    private readonly Catalog _menu = new(gin: 20, fernet: 20);

    private readonly OrdersInMemory _orders = new();

    [Fact]
    public async Task HandleAsync_WhenEverythingIsInPlace_ConfirmsThePaidOrderIntoTheQueue()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("K-4821", result.Value.Code);
        Assert.Equal(TheVenue, _orders.Added!.VenueId);
        Assert.Equal("María Quadro", _orders.Added.CustomerName);
        Assert.Equal(OrderStatus.Queued, _orders.Added.Status);
        Assert.Equal(Tonight, _orders.Added.PaidAt);
    }

    /// <summary>
    /// The heart of it: what anything costs is read from this venue's menu, not
    /// from what the phone said. Otherwise anybody pays what they like.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Always_PricesTheOrderFromTheVenuesOwnMenu()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            AnOrderOf(Two(_menu.Gin), One(_menu.Fernet)), CancellationToken.None);

        OrderItem gin = _orders.Added!.Items.Single(item => item.ProductId == _menu.Gin.Id);

        Assert.Equal("Gin Tonic", gin.ProductName);
        Assert.Equal(4500m, gin.UnitPrice);
        Assert.Equal(13000m, result.Value.Total);
    }

    [Fact]
    public async Task HandleAsync_WhenALineCarriesANote_KeepsItOnThatDrink()
    {
        await AHandler().HandleAsync(ATwoGinOrder(note: "sin hielo"), CancellationToken.None);

        Assert.Equal("sin hielo", _orders.Added!.Items.Single().Note);
    }

    [Fact]
    public async Task HandleAsync_WhenItIsConfirmed_TakesTheDrinksOutOfStock()
    {
        await AHandler().HandleAsync(AnOrderOf(Two(_menu.Gin), One(_menu.Fernet)), CancellationToken.None);

        Assert.Equal(18, _menu.Gin.Stock);
        Assert.Equal(19, _menu.Fernet.Stock);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNameIsNotAFullName_FailsWithoutTouchingAnything()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder(name: "Euge"), CancellationToken.None);

        Assert.Equal(CustomerNamePolicy.NeedsASurname.Code, result.Error!.Code);
        Assert.Null(_orders.Added);
        Assert.Equal(20, _menu.Gin.Stock);
    }

    [Fact]
    public async Task HandleAsync_WhenThereIsNothingInIt_FailsAsEmpty()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            new ConfirmOrderCommand("María Quadro", PaymentMethod.Digital, "abc-123", []),
            CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.Empty.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task HandleAsync_WhenThereIsNoIdempotencyKey_Refuses(string? key)
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder(key: key), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.IdempotencyKeyRequired.Code, result.Error!.Code);
    }

    // She chose this on 2026-09-17: nobody pays for an order they will not get
    // in full. It is rejected whole, and the screen says which drink.
    [Fact]
    public async Task HandleAsync_WhenThereIsNotEnoughStock_RejectsTheWholeOrderAndNamesTheDrink()
    {
        Catalog menu = new(gin: 1, fernet: 5);

        Result<ConfirmedOrder> result = await AHandlerOver(menu).HandleAsync(
            AnOrderOf(Two(menu.Gin), One(menu.Fernet)), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Contains("Gin Tonic", result.Error.Message, StringComparison.Ordinal);
        Assert.Null(_orders.Added);
    }

    // And nothing else moves either: the Fernet that was fine stays on the
    // shelf, because the order it belonged to never happened.
    [Fact]
    public async Task HandleAsync_WhenOneDrinkIsShort_LeavesTheOthersOnTheShelf()
    {
        Catalog menu = new(gin: 1, fernet: 5);

        await AHandlerOver(menu).HandleAsync(
            AnOrderOf(Two(menu.Gin), One(menu.Fernet)), CancellationToken.None);

        Assert.Equal(5, menu.Fernet.Stock);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDrinkIsNotOnTheMenuAnyMore_RejectsTheWholeOrder()
    {
        Catalog.TurnOff(_menu.Gin, nameof(Product.IsActive));

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.NotOnTheMenu.Code, result.Error!.Code);
        Assert.Contains("Gin Tonic", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDrinkIsOffTonight_RejectsTheWholeOrder()
    {
        Catalog.TurnOff(_menu.Gin, nameof(Product.IsAvailable));

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.NotOnTheMenu.Code, result.Error!.Code);
    }

    // A product id this venue never had: the same answer as one it removed.
    // Nothing here tells a customer what another venue sells.
    [Fact]
    public async Task HandleAsync_WhenTheDrinkBelongsToNobody_RejectsTheWholeOrder()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            AnOrderOf(new OrderLineRequest(Guid.CreateVersion7(), 1, null)), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.NotOnTheMenu.Code, result.Error!.Code);
    }

    /// <summary>
    /// Criterion 6: the signal is slow, the phone or the browser retries, and
    /// the same order lands again. It answers with the one it already made —
    /// the same code, so whoever is waiting sees no difference.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheKeyWasAlreadyUsed_HandsBackTheSameOrder()
    {
        ConfirmOrderHandler handler = AHandler();

        Result<ConfirmedOrder> first = await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);
        Result<ConfirmedOrder> second = await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(first.Value.Code, second.Value.Code);
        Assert.Equal(1, _orders.TimesAdded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheKeyWasAlreadyUsed_DoesNotTakeTheDrinksTwice()
    {
        ConfirmOrderHandler handler = AHandler();

        await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);
        await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(18, _menu.Gin.Stock);
    }

    // A different key is a different order, even with the same drinks: the
    // second round of the night has to be orderable.
    [Fact]
    public async Task HandleAsync_WhenTheKeyIsNew_MakesAnotherOrder()
    {
        ConfirmOrderHandler handler = AHandler();

        await handler.HandleAsync(ATwoGinOrder(key: "abc-123"), CancellationToken.None);
        await handler.HandleAsync(ATwoGinOrder(key: "def-456"), CancellationToken.None);

        Assert.Equal(2, _orders.TimesAdded);
    }

    // The brief only lets us build the digital one. The other two are drawn on
    // the screen and switched off; anybody asking the API for them gets this.
    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.VipBalance)]
    public async Task HandleAsync_WhenThePaymentMethodIsNotBuiltYet_SaysSo(PaymentMethod method)
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder(method: method), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.PaymentMethodUnavailable.Code, result.Error!.Code);
    }

    private static OrderLineRequest One(Product product) => new(product.Id, 1, null);

    private static OrderLineRequest Two(Product product) => new(product.Id, 2, null);

    private ConfirmOrderCommand ATwoGinOrder(
        string? name = "María Quadro",
        string? key = "abc-123",
        PaymentMethod method = PaymentMethod.Digital,
        string? note = null) =>
        new(name, method, key, [new OrderLineRequest(_menu.Gin.Id, 2, note)]);

    private static ConfirmOrderCommand AnOrderOf(params OrderLineRequest[] lines) =>
        new("María Quadro", PaymentMethod.Digital, "abc-123", lines);

    private ConfirmOrderHandler AHandler() => AHandlerOver(_menu);

    private ConfirmOrderHandler AHandlerOver(Catalog menu) =>
        new(_orders,
            menu,
            new SequenceThatAnswers("K-4821"),
            [new DigitalPaymentStrategy(new FixedClock(Tonight))],
            new TheVenueIsFixed(TheVenue));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TheVenueIsFixed(Guid id) : ICurrentVenue
    {
        public Guid Id => id;
    }

    private sealed class SequenceThatAnswers(string code) : IOrderCodeSequence
    {
        public Task<OrderCode> NextAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OrderCode.Parse(code));
    }

    private sealed class OrdersInMemory : IOrderRepository
    {
        private readonly Dictionary<string, Order> _byKey = [];

        public Order? Added { get; private set; }

        public int TimesAdded { get; private set; }

        public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_byKey.GetValueOrDefault(key));

        public Task AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken)
        {
            Added = order;
            TimesAdded += 1;
            _byKey[idempotencyKey] = order;

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The venue's menu, as the handler is allowed to see it. It holds the real
    /// products, so the ids the tests ask for are the ones the domain generated
    /// and nothing here has to invent one.
    /// </summary>
    private sealed class Catalog(int gin, int fernet) : IProductsForOrdering
    {
        public Product Gin { get; } = Product.Create(TheVenue, "Gin Tonic", null, null, 4500m, gin);

        public Product Fernet { get; } = Product.Create(TheVenue, "Fernet con Coca", null, null, 4000m, fernet);

        /// <summary>
        /// Takes a product off the menu, the way US-07 and US-08 will once they
        /// are built. Until then the domain has no method for it, and inventing
        /// one so that a test can call it would be writing the next story's
        /// code today.
        /// </summary>
        public static void TurnOff(Product product, string flag) =>
            typeof(Product).GetProperty(flag)!
                .GetSetMethod(nonPublic: true)!
                .Invoke(product, [false]);

        public Task<IReadOnlyList<Product>> GetForOrderingAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Product>>(
                [.. new[] { Gin, Fernet }.Where(product => ids.Contains(product.Id))]);

        // The double holds the products themselves, so what the real one writes
        // to the database has already happened here.
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
