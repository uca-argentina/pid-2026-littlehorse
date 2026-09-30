using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Payments;
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

    private const string CheckoutUrl = "https://www.mercadopago.com.ar/checkout/v1/redirect?pref_id=123";

    // US-24: paid through Mercado Pago, the order waits and the customer is
    // told where to pay it — and that is kept with the order.
    [Fact]
    public async Task HandleAsync_WhenTheMethodHandsOff_AnswersWhereToPayAndKeepsIt()
    {
        HandsOffToAGateway payment = new(CheckoutUrl);

        Result<ConfirmedOrder> result = await AHandlerPaidBy(payment).HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(CheckoutUrl, result.Value.PaymentUrl);
        Assert.Equal(OrderStatus.AwaitingPayment, result.Value.Status);
        Assert.Null(result.Value.PaidAt);
        Assert.Equal("bar-alfa", payment.VenueSlug);
        Assert.Same(_orders.Added, _orders.Saved.Single());
        Assert.Equal(CheckoutUrl, _orders.Added!.PaymentUrl);
    }

    // The gateway is down: nobody can pay, so the order must not hold the
    // drinks it took off the menu while it waits for nothing.
    [Fact]
    public async Task HandleAsync_WhenTheHandOffFails_CancelsTheOrderAndGivesItsStockBack()
    {
        Result<ConfirmedOrder> result = await AHandlerPaidBy(new HandsOffToAGateway(fails: true))
            .HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(PaymentErrors.GatewayUnavailable, result.Error);
        Assert.Equal(OrderStatus.Canceled, _orders.Added!.Status);
        Assert.Same(_orders.Added, _orders.StockReturned.Single());
    }

    // A retry over a bad signal: the same checkout, never a second one that
    // could be paid as well.
    [Fact]
    public async Task HandleAsync_WhenTheSameAttemptArrivesAgain_AnswersTheSameCheckoutWithoutOpeningAnother()
    {
        HandsOffToAGateway payment = new(CheckoutUrl);
        ConfirmOrderHandler handler = AHandlerPaidBy(payment);

        await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);
        Result<ConfirmedOrder> again = await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(CheckoutUrl, again.Value.PaymentUrl);
        Assert.Equal(1, payment.HandOffs);
    }

    /// <summary>A method that waits for a gateway, standing in for the digital one.</summary>
    private sealed class HandsOffToAGateway(string? url = null, bool fails = false) : IPaymentStrategy, IHandsOffPayment
    {
        public int HandOffs { get; private set; }

        public string? VenueSlug { get; private set; }

        public PaymentMethod Method => PaymentMethod.Digital;

        public void Settle(Order order) => order.AwaitPayment(Method);

        public Task<Result<string?>> HandOffAsync(Order order, string venueSlug, CancellationToken cancellationToken)
        {
            HandOffs += 1;
            VenueSlug = venueSlug;

            if (fails) return Task.FromResult<Result<string?>>(PaymentErrors.GatewayUnavailable);

            order.OfferPaymentAt(url!);

            return Task.FromResult<Result<string?>>(url);
        }
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

    /*
     * Taking the drinks off the shelf is not tested here any more: the handler
     * decides that the order can be served and the repository writes it, with
     * one conditional statement per drink. What that statement does belongs to
     * a real database, and ConfirmOrderTests in the integration suite is where
     * it is proved — together with two customers reaching for the last one.
     */

    [Fact]
    public async Task HandleAsync_WhenTheNameIsNotAFullName_FailsWithoutTouchingAnything()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder(name: "Euge"), CancellationToken.None);

        Assert.Equal(CustomerNamePolicy.NeedsASurname.Code, result.Error!.Code);
        Assert.Null(_orders.Added);
    }

    [Fact]
    public async Task HandleAsync_WhenThereIsNothingInIt_FailsAsEmpty()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            new ConfirmOrderCommand("María Quadro", PaymentMethod.Digital, "abc-123", [], "bar-alfa"),
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

    /// <summary>
    /// Two lines of the same drink. Refused before a code is asked for, because
    /// asking spends one — and because the stock check reads the same number
    /// twice and would let through more than there is.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheSameDrinkComesOnTwoLines_RefusesWithoutSpendingACode()
    {
        SequenceThatCounts sequence = new();

        Result<ConfirmedOrder> result = await AHandlerOver(_menu, sequence).HandleAsync(
            AnOrderOf(One(_menu.Gin), Two(_menu.Gin)), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.DuplicateLine.Code, result.Error!.Code);
        Assert.Equal(0, sequence.TimesAsked);
    }

    /// <summary>
    /// Somebody else sold from the same drink while this order was being
    /// priced. A packed venue has everybody ordering the same three drinks at
    /// once, so losing that race has to be invisible: the order is rebuilt
    /// against the menu as it is now and goes through.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheStockMovedUnderneath_BuildsTheOrderAgainAndSucceeds()
    {
        _orders.RefusesTheFirst(OrderErrors.StockMoved);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _orders.TimesAdded);
    }

    // The same code, not a fresh one per attempt: asking again would leave a
    // hole in the venue's numbering for every race anybody loses.
    [Fact]
    public async Task HandleAsync_WhenItHasToTryAgain_KeepsTheCodeItWasGiven()
    {
        SequenceThatCounts sequence = new();
        _orders.RefusesTheFirst(OrderErrors.StockMoved);

        Result<ConfirmedOrder> result = await AHandlerOver(_menu, sequence).HandleAsync(
            ATwoGinOrder(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, sequence.TimesAsked);
    }

    // And when it never stops moving, the customer is told to look at their
    // order rather than kept waiting while we try for ever.
    [Fact]
    public async Task HandleAsync_WhenTheStockKeepsMoving_GivesUpAndSaysSo()
    {
        _orders.RefusesAlways(OrderErrors.StockMoved);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(OrderErrors.StockMoved.Code, result.Error!.Code);
    }

    /// <summary>
    /// A line asking for none of something, or for minus one. The screen cannot
    /// produce it, a hand-rolled request can, and it used to slip past the
    /// stock check — nothing is ever less than zero — and blow up inside the
    /// domain with a problem type meant for us rather than for the customer.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task HandleAsync_WhenALineAsksForNothing_Refuses(int quantity)
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            AnOrderOf(new OrderLineRequest(_menu.Gin.Id, quantity, null)), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.QuantityNotPositive.Code, result.Error!.Code);
        Assert.Null(_orders.Added);
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
        Assert.Equal("Gin Tonic", result.Error.Subject);
        Assert.Null(_orders.Added);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDrinkIsNotOnTheMenuAnyMore_RejectsTheWholeOrder()
    {
        Catalog.TakeOffTheMenu(_menu.Gin);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.NotOnTheMenu.Code, result.Error!.Code);
        Assert.Equal("Gin Tonic", result.Error.Subject);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDrinkIsOffTonight_RejectsTheWholeOrder()
    {
        _menu.Gin.MarkUnavailable();

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

    // The VIP tables are out of this sprint: drawn on the screen and switched
    // off, and anybody asking the API for them gets this.
    [Fact]
    public async Task HandleAsync_WhenThePaymentMethodIsNotBuiltYet_SaysSo()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder(method: PaymentMethod.VipBalance), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.PaymentMethodUnavailable.Code, result.Error!.Code);
    }

    // US-24, criterion 3: cash leaves the order waiting at the till, unpaid,
    // and nowhere near the bar until a cashier takes the money.
    [Fact]
    public async Task HandleAsync_WhenPaidInCash_LeavesTheOrderAwaitingPaymentAtTheTill()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder(method: PaymentMethod.Cash), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.AwaitingPayment, result.Value.Status);
        Assert.Null(result.Value.PaidAt);
        Assert.Equal(PaymentMethod.Cash, _orders.Added!.Method);
        Assert.IsType<OrderAwaitingPayment>(Assert.Single(_orders.Added.DomainEvents));
    }

    /// <summary>
    /// How far the order gets is the strategy's call, not the handler's.
    /// </summary>
    /// <remarks>
    /// The three ways of paying do not end in the same place. Digital and VIP
    /// settle there and then and the drinks go straight to the bar; cash leaves
    /// the order waiting at the till (§6 of the functional design), and putting
    /// it in the queue before the cashier has taken the money would send drinks
    /// to the bar that nobody paid for. If the handler is the one that enqueues,
    /// building the cash method means editing the handler — which is the one
    /// thing the Strategy is here to prevent.
    /// </remarks>
    [Fact]
    public async Task HandleAsync_WhenTheStrategyDoesNotQueueTheOrder_LeavesItWhereTheStrategyPutIt()
    {
        ConfirmOrderHandler handler = AHandlerPaidBy(new SettlesWithoutQueueing(Tonight));

        Result<ConfirmedOrder> result = await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, _orders.Added!.Status);
    }

    private static OrderLineRequest One(Product product) => new(product.Id, 1, null);

    private static OrderLineRequest Two(Product product) => new(product.Id, 2, null);

    private ConfirmOrderCommand ATwoGinOrder(
        string? name = "María Quadro",
        string? key = "abc-123",
        PaymentMethod method = PaymentMethod.Digital,
        string? note = null) =>
        new(name, method, key, [new OrderLineRequest(_menu.Gin.Id, 2, note)], "bar-alfa");

    private static ConfirmOrderCommand AnOrderOf(params OrderLineRequest[] lines) =>
        new("María Quadro", PaymentMethod.Digital, "abc-123", lines, "bar-alfa");

    private ConfirmOrderHandler AHandler() => AHandlerOver(_menu);

    private ConfirmOrderHandler AHandlerPaidBy(IPaymentStrategy payment) =>
        new(_orders,
            _menu,
            new SequenceThatAnswers("K-4821"),
            [payment],
            new TheVenueIsFixed(TheVenue));

    private ConfirmOrderHandler AHandlerOver(Catalog menu, IOrderCodeSequence? codes = null) =>
        new(_orders,
            menu,
            codes ?? new SequenceThatAnswers("K-4821"),
            [new SettlesOnTheSpot(Tonight), new CashPaymentStrategy()],
            new TheVenueIsFixed(TheVenue));

    /// <summary>
    /// A method that pays and sends the order to the bar the moment it is
    /// confirmed — what digital did until it went through Mercado Pago (US-24).
    /// These tests are about pricing, stock and retries, not about any one way
    /// of paying, so they keep it.
    /// </summary>
    private sealed class SettlesOnTheSpot(DateTimeOffset now) : IPaymentStrategy
    {
        public PaymentMethod Method => PaymentMethod.Digital;

        public void Settle(Order order)
        {
            order.Pay(now, Method);
            order.Enqueue();
        }
    }

    /// <summary>
    /// A method that settles the money and stops, without sending the order
    /// to the bar: proves the handler never queues on its own.
    /// </summary>
    private sealed class SettlesWithoutQueueing(DateTimeOffset now) : IPaymentStrategy
    {
        public PaymentMethod Method => PaymentMethod.Digital;

        public void Settle(Order order) => order.Pay(now, Method);
    }

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

    /// <summary>Counts what it was asked, so a wasted code is visible.</summary>
    private sealed class SequenceThatCounts : IOrderCodeSequence
    {
        public int TimesAsked { get; private set; }

        public Task<OrderCode> NextAsync(CancellationToken cancellationToken)
        {
            TimesAsked += 1;

            return Task.FromResult(OrderCode.Parse("K-4821"));
        }
    }

    private sealed class OrdersInMemory : IOrderRepository
    {
        private readonly Dictionary<string, Order> _byKey = [];

        private Error? _refusal;

        private bool _onlyOnce;

        public Order? Added { get; private set; }

        public int TimesAdded { get; private set; }

        public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_byKey.GetValueOrDefault(key));

        /// <summary>Refuses the first write, the way a stock somebody else moved does.</summary>
        public void RefusesTheFirst(Error refusal)
        {
            _refusal = refusal;
            _onlyOnce = true;
        }

        public void RefusesAlways(Error refusal)
        {
            _refusal = refusal;
            _onlyOnce = false;
        }

        public Task<Result<Order>> AddAsync(
            Order order,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            if (_refusal is not null)
            {
                Error refusal = _refusal;

                if (_onlyOnce) _refusal = null;

                return Task.FromResult<Result<Order>>(refusal);
            }

            Added = order;
            TimesAdded += 1;
            _byKey[idempotencyKey] = order;

            return Task.FromResult<Result<Order>>(order);
        }

        public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Confirming never loads an existing order.");

        public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Confirming never loads an existing order.");

        public List<Order> Saved { get; } = [];

        public List<Order> StockReturned { get; } = [];

        public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken)
        {
            Saved.Add(order);
            return Task.FromResult<Result<Order>>(order);
        }

        public Task SaveReturningStockAsync(Order order, CancellationToken cancellationToken)
        {
            StockReturned.Add(order);
            return Task.CompletedTask;
        }

        public Task<Order?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Confirming never loads an existing order.");

        public Task<IReadOnlyList<Order>> GetAwaitingPaymentCreatedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Confirming never looks for expired orders.");
    }

    /// <summary>
    /// The venue's menu, as the handler is allowed to see it. It holds the real
    /// products, so the ids the tests ask for are the ones the domain generated
    /// and nothing here has to invent one.
    /// </summary>
    private sealed class Catalog(int gin, int fernet) : IProductsForOrdering
    {
        public Product Gin { get; } = Product.Create(TheVenue, "Gin Tonic", null, null, 4500m, gin, Guid.CreateVersion7());

        public Product Fernet { get; } = Product.Create(TheVenue, "Fernet con Coca", null, null, 4000m, fernet, Guid.CreateVersion7());

        /// <summary>
        /// The soft delete, written from outside because US-08 has not built
        /// the domain method yet. US-07's switch no longer needs this: it is
        /// Product.MarkUnavailable, and the test below calls it. This one goes
        /// the same way once a product can be taken off the menu for good.
        /// </summary>
        public static void TakeOffTheMenu(Product product) =>
            typeof(Product).GetProperty(nameof(Product.IsActive))!
                .GetSetMethod(nonPublic: true)!
                .Invoke(product, [false]);

        public Task<IReadOnlyList<Product>> GetForOrderingAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Product>>(
                [.. new[] { Gin, Fernet }.Where(product => ids.Contains(product.Id))]);
    }
}
