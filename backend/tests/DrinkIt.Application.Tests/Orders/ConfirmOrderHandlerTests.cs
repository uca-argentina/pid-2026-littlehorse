using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Promotions;
using DrinkIt.Application.StampCards;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Promotions;
using DrinkIt.Domain.StampCards;

namespace DrinkIt.Application.Tests.Orders;

public class ConfirmOrderHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Tonight = new(2026, 9, 17, 2, 30, 0, TimeSpan.Zero);

    // xUnit builds one of these per test, so every test gets its own menu and
    // its own orders: nothing one of them sells is missing from the next.
    private readonly Catalog _menu = new(gin: 20, fernet: 20);

    private readonly OrdersInMemory _orders = new();

    private readonly PromotionsOn _promotions = new();

    private readonly StampCardsInMemory _cards = new();

    private static readonly Guid TheNight = Guid.CreateVersion7();

    /// <summary>On by default: most of what is tested here happens during a night.</summary>
    private readonly TonightIs _nights = new(TheNight);

    // US-35, criterion 2: the order belongs to the night it was placed in.
    [Fact]
    public async Task HandleAsync_WhenANightIsUnderway_PlacesTheOrderInIt()
    {
        await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(TheNight, _orders.Added!.NightId);
    }

    // Asked with the server's clock: the phone's would let anybody order at
    // 07:00 by setting their own time back.
    [Fact]
    public async Task HandleAsync_Always_AsksForTheNightAtTheServersMoment()
    {
        await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(Tonight, _nights.AskedAt);
    }

    // US-35, criterion 3, and the edge in its notes: a cart put together at
    // 05:58 and confirmed at 06:01 gets the same answer.
    [Fact]
    public async Task HandleAsync_WhenNoNightIsUnderway_RefusesWithoutPlacingAnything()
    {
        _nights.Close();
        SequenceThatCounts sequence = new();

        Result<ConfirmedOrder> result = await AHandlerOver(_menu, sequence).HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(OrderErrors.NotTakingOrders, result.Error);
        Assert.Null(_orders.Added);
        Assert.Equal(0, sequence.TimesAsked);
    }

    // A retry of an order placed at 05:59 that lands at 06:01 is the same
    // order, not a new one: it is answered as before, not refused.
    [Fact]
    public async Task HandleAsync_WhenTheNightClosedBeforeARetryOfAnOrderItAlreadyHas_HandsBackThatOrder()
    {
        ConfirmOrderHandler handler = AHandler();
        Result<ConfirmedOrder> first = await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);

        _nights.Close();
        Result<ConfirmedOrder> retry = await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(first.Value.Code, retry.Value.Code);
    }

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

    // US-41, criterion 3: another phone of the same table spent at the same
    // moment. Going round again reads the balance as it is now.
    [Fact]
    public async Task HandleAsync_WhenTheTablesBalanceMovedUnderneath_BuildsTheOrderAgainAndSucceeds()
    {
        _orders.RefusesTheFirst(VipAccountErrors.BalanceMoved);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _orders.TimesAdded);
    }

    // What refuses the money is the method's call; the handler only passes it on.
    [Fact]
    public async Task HandleAsync_WhenThePaymentIsRefused_FailsWithItsReasonAndWritesNothing()
    {
        Result<ConfirmedOrder> result = await AHandlerPaidBy(new Refuses(VipAccountErrors.BalanceChanged))
            .HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(VipAccountErrors.BalanceChanged, result.Error);
        Assert.Null(_orders.Added);
    }

    [Fact]
    public async Task HandleAsync_WhenATableCodeIsSent_HandsItToThePayment()
    {
        PaysOnTheSpot payment = new(Tonight);

        await AHandlerPaidBy(payment).HandleAsync(ATwoGinOrder() with { TableCode = "the-code" }, CancellationToken.None);

        Assert.Equal("the-code", payment.Request!.TableCode);
    }

    // US-42: what the phone agreed to pay on top of the balance, untouched.
    [Fact]
    public async Task HandleAsync_WhenADifferenceIsAgreed_HandsItToThePayment()
    {
        PaysOnTheSpot payment = new(Tonight);

        await AHandlerPaidBy(payment).HandleAsync(ATwoGinOrder() with { TableCode = "the-code", DigitalAmount = 4_000m }, CancellationToken.None);

        Assert.Equal(4_000m, payment.Request!.DigitalAmount);
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

    // US-36 and US-37: a paid order goes to the bar with less work among those
    // that have all of it, and is taken from that bar's stock.
    [Fact]
    public async Task HandleAsync_WhenTwoBarsHaveItAll_SendsTheOrderToTheOneWithLessWork()
    {
        BarSupply busy = new(Guid.CreateVersion7(), 4, new Dictionary<Guid, int> { [_menu.Gin.Id] = 10 });
        BarSupply quiet = new(Guid.CreateVersion7(), 1, new Dictionary<Guid, int> { [_menu.Gin.Id] = 10 });
        Bars bars = new(busy, quiet);

        Result<ConfirmedOrder> result = await AHandlerOver(_menu, bars: bars).HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(quiet.KdsId, _orders.Added!.KdsId);
        Assert.Equal(TheNight, bars.AskedFor);
    }

    // US-38: a bar whose tablet stopped beating two minutes ago, by the
    // server's clock, gets nothing however little work it has.
    [Fact]
    public async Task HandleAsync_WhenTheQuieterBarIsDisconnected_SendsTheOrderToTheConnectedOne()
    {
        BarSupply busy = new(Guid.CreateVersion7(), 4, new Dictionary<Guid, int> { [_menu.Gin.Id] = 10 }, Tonight.AddSeconds(-20));
        BarSupply quiet = new(Guid.CreateVersion7(), 1, new Dictionary<Guid, int> { [_menu.Gin.Id] = 10 }, Tonight.AddMinutes(-2));

        await AHandlerOver(_menu, bars: new Bars(quiet, busy)).HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(busy.KdsId, _orders.Added!.KdsId);
    }

    // The gins are in one bar and the fernet in the other: an order is not split
    // between two bars, so nobody would have it all.
    [Fact]
    public async Task HandleAsync_WhenNoBarHasItAll_RejectsTheWholeOrderAndNamesADrink()
    {
        Bars bars = new(
            new BarSupply(Guid.CreateVersion7(), 0, new Dictionary<Guid, int> { [_menu.Gin.Id] = 10 }),
            new BarSupply(Guid.CreateVersion7(), 0, new Dictionary<Guid, int> { [_menu.Fernet.Id] = 10 }));

        Result<ConfirmedOrder> result = await AHandlerOver(_menu, bars: bars).HandleAsync(
            AnOrderOf(Two(_menu.Gin), One(_menu.Fernet)), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Equal("Fernet con Coca", result.Error.Subject);
        Assert.Null(_orders.Added);
    }

    // A night whose bars have nothing loaded sells nothing.
    [Fact]
    public async Task HandleAsync_WhenTheNightHasNoBarWithStock_RejectsAndNamesTheDrink()
    {
        Result<ConfirmedOrder> result = await AHandlerOver(_menu, bars: new Bars()).HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Equal("Gin Tonic", result.Error.Subject);
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

    // US-36: the bar is chosen when the money is in. Cash has not paid yet, so
    // it holds no bar and takes no stock until the till collects it.
    [Fact]
    public async Task HandleAsync_WhenPaidInCash_GoesToNoBarYet()
    {
        await AHandler().HandleAsync(ATwoGinOrder(method: PaymentMethod.Cash), CancellationToken.None);

        Assert.Null(_orders.Added!.KdsId);
    }

    // Still checked, so nobody walks to the till for a drink no bar has.
    [Fact]
    public async Task HandleAsync_WhenPaidInCashAndNoBarHasItAll_RejectsAndNamesTheDrink()
    {
        Result<ConfirmedOrder> result = await AHandlerOver(_menu, bars: new Bars())
            .HandleAsync(ATwoGinOrder(method: PaymentMethod.Cash), CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Null(_orders.Added);
    }

    /// <summary>
    /// Whether the money is in is the strategy's call; what follows once it is,
    /// is the same for every way of paying (CLAUDE.md: they converge on the
    /// same flow after Paid).
    /// </summary>
    /// <remarks>
    /// A new way of paying that settles on the spot is only a class that pays:
    /// queuing it into a bar is not its business, so it cannot forget to.
    /// Cash, which does not pay here, stays at the till (the test above).
    /// </remarks>
    [Fact]
    public async Task HandleAsync_WhenTheStrategyPaysOnTheSpot_QueuesTheOrderIntoABar()
    {
        ConfirmOrderHandler handler = AHandlerPaidBy(new PaysOnTheSpot(Tonight));

        Result<ConfirmedOrder> result = await handler.HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Queued, _orders.Added!.Status);
        Assert.NotNull(_orders.Added.KdsId);
    }

    // Tonight is 23:30 of a Wednesday in Buenos Aires (02:30 of Thursday UTC).
    private Promotion AWednesdayNightPromotion(int percent, string name = "Happy hour", params Product[] products) =>
        Promotion.Create(TheVenue, name, percent, [DayOfWeek.Wednesday], new TimeOnly(22, 0), new TimeOnly(1, 0), products.Length == 0 ? [_menu.Gin.Id] : [.. products.Select(product => product.Id)]);

    // US-55, criterion 1.
    [Fact]
    public async Task HandleAsync_WhenAPromotionIsOn_PricesTheItemAtTheDiscountAndKeepsTheListPrice()
    {
        _promotions.Add(AWednesdayNightPromotion(30));

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        OrderItem gin = _orders.Added!.Items.Single();

        Assert.Equal(3150m, gin.UnitPrice);
        Assert.Equal(4500m, gin.RegularPrice);
        Assert.Equal("Happy hour", gin.PromotionName);
        Assert.Equal(6300m, result.Value.Total);
    }

    [Fact]
    public async Task HandleAsync_WhenAPromotionCoversAnotherProduct_LeavesThePriceAlone()
    {
        _promotions.Add(AWednesdayNightPromotion(30, products: _menu.Fernet));

        await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(4500m, _orders.Added!.Items.Single().UnitPrice);
        Assert.Null(_orders.Added.Items.Single().PromotionName);
    }

    // US-55, criterion 2: the biggest, never the two together.
    [Fact]
    public async Task HandleAsync_WhenTwoPromotionsAreOn_AppliesOnlyTheBiggerDiscount()
    {
        _promotions.Add(AWednesdayNightPromotion(20, "Twenty"));
        _promotions.Add(AWednesdayNightPromotion(30, "Thirty"));

        await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        OrderItem gin = _orders.Added!.Items.Single();

        Assert.Equal(3150m, gin.UnitPrice);
        Assert.Equal("Thirty", gin.PromotionName);
    }

    [Fact]
    public async Task HandleAsync_WhenThePromotionWindowIsClosed_ChargesTheListPrice()
    {
        _promotions.Add(Promotion.Create(TheVenue, "Lunch", 30, [DayOfWeek.Wednesday], new TimeOnly(12, 0), new TimeOnly(15, 0), [_menu.Gin.Id]));

        await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(4500m, _orders.Added!.Items.Single().UnitPrice);
        Assert.Null(_orders.Added.Items.Single().RegularPrice);
    }

    // The server runs in UTC: at 02:30 UTC it is Thursday there and still
    // Wednesday in the venue. A Thursday-only promotion must not be on.
    [Fact]
    public async Task HandleAsync_WhenItIsAnotherDayInUtcButNotAtTheVenue_DecidesWithTheVenuesClock()
    {
        _promotions.Add(Promotion.Create(TheVenue, "Thursday", 30, [DayOfWeek.Thursday], new TimeOnly(0, 0), new TimeOnly(6, 0), [_menu.Gin.Id]));

        await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(4500m, _orders.Added!.Items.Single().UnitPrice);
    }

    [Fact]
    public async Task HandleAsync_WhenThePromotionIsDeactivated_ChargesTheListPrice()
    {
        Promotion promotion = AWednesdayNightPromotion(30);
        promotion.Deactivate();
        _promotions.Add(promotion);

        await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Equal(4500m, _orders.Added!.Items.Single().UnitPrice);
    }

    // US-55, criterion 4: the cart said 9000, the price is now 6300.
    [Fact]
    public async Task HandleAsync_WhenTheTotalTheCustomerSawIsNotTheTotalNow_RefusesWithTheNewOneAndWritesNothing()
    {
        _promotions.Add(AWednesdayNightPromotion(30));

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder() with { ExpectedTotal = 9000m }, CancellationToken.None);

        Assert.Equal(OrderErrors.PriceChangedCode, result.Error!.Code);
        Assert.Contains("6300", result.Error.Message, StringComparison.Ordinal);
        Assert.Null(_orders.Added);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTotalTheCustomerSawIsTheTotalNow_Confirms()
    {
        _promotions.Add(AWednesdayNightPromotion(30));

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder() with { ExpectedTotal = 6300m }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WhenNoTotalIsSent_DoesNotCheckIt()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // US-58, criterion 1: the first order of a phone is given a card.
    [Fact]
    public async Task HandleAsync_WhenTheCardIsOnAndThePhoneHasNone_IssuesOneAndHandsOverItsToken()
    {
        _cards.RuleIs(5, _menu.Fernet);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        StampCard issued = Assert.Single(_cards.Issued);

        Assert.Equal(issued.Token, result.Value.StampCardToken);
        Assert.Equal(issued.Id, _orders.Added!.StampCardId);
        Assert.Equal(TheVenue, issued.VenueId);
    }

    [Fact]
    public async Task HandleAsync_WhenThePhoneBringsItsCard_CountsTheOrderForThatCardAndIssuesNoOther()
    {
        _cards.RuleIs(5, _menu.Fernet);
        StampCard card = _cards.Has(earned: 2);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder() with { StampCardToken = card.Token }, CancellationToken.None);

        Assert.Equal(card.Id, _orders.Added!.StampCardId);
        Assert.Equal(card.Token, result.Value.StampCardToken);
        Assert.Empty(_cards.Issued);
    }

    // US-58, criterion 4: a token that is not this venue's finds nothing, so it
    // earns nothing anywhere; the phone is given a card of this venue.
    [Fact]
    public async Task HandleAsync_WhenTheTokenIsNotOfThisVenue_IssuesANewCardInsteadOfUsingIt()
    {
        _cards.RuleIs(5, _menu.Fernet);
        string foreign = StampCard.Issue(Guid.CreateVersion7()).Token;

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder() with { StampCardToken = foreign }, CancellationToken.None);

        Assert.NotEqual(foreign, result.Value.StampCardToken);
        Assert.Single(_cards.Issued);
        Assert.Equal(_cards.Issued[0].Id, _orders.Added!.StampCardId);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTokenIsMalformed_IssuesANewCard()
    {
        _cards.RuleIs(5, _menu.Fernet);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder() with { StampCardToken = "not-a-token" }, CancellationToken.None);

        Assert.Single(_cards.Issued);
        Assert.Equal(_cards.Issued[0].Token, result.Value.StampCardToken);
    }

    // US-57, criterion 3 and the note: without a rule there is no card.
    [Fact]
    public async Task HandleAsync_WhenTheVenueHasNoRule_GivesNoCard()
    {
        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder(), CancellationToken.None);

        Assert.Null(result.Value.StampCardToken);
        Assert.Null(_orders.Added!.StampCardId);
        Assert.Empty(_cards.Issued);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCardIsSwitchedOff_GivesNoCardAndEarnsNothing()
    {
        _cards.RuleIs(5, _menu.Fernet, active: false);
        StampCard card = _cards.Has(earned: 2);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder() with { StampCardToken = card.Token }, CancellationToken.None);

        Assert.Null(result.Value.StampCardToken);
        Assert.Null(_orders.Added!.StampCardId);
        Assert.Empty(_cards.Issued);
    }

    // US-59, criterion 1.
    [Fact]
    public async Task HandleAsync_WhenTheCardIsCompleteAndTheRewardIsAsked_AddsTheRewardAtZeroAndClaimsIt()
    {
        _cards.RuleIs(5, _menu.Fernet);
        StampCard card = _cards.Has(earned: 5);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true }, CancellationToken.None);

        OrderItem reward = _orders.Added!.Items.Single(item => item.IsReward);

        Assert.Equal(_menu.Fernet.Id, reward.ProductId);
        Assert.Equal(0m, reward.UnitPrice);
        Assert.Equal(5, reward.StampsSpent);
        Assert.Equal(9000m, result.Value.Total);
        Assert.Equal(1, card.RewardsClaimed);
    }

    // The reward is priced by the server: nothing in the request says what it costs.
    [Fact]
    public async Task HandleAsync_WhenTheRewardIsAsked_TheCostComesFromTheRuleNotFromThePhone()
    {
        _cards.RuleIs(8, _menu.Fernet);
        StampCard card = _cards.Has(earned: 9);

        await AHandler().HandleAsync(ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true }, CancellationToken.None);

        Assert.Equal(8, _orders.Added!.Items.Single(item => item.IsReward).StampsSpent);
    }

    [Fact]
    public async Task HandleAsync_WhenTheRewardIsTheSameDrinkAsAPaidLine_KeepsBothLines()
    {
        _cards.RuleIs(5, _menu.Gin);
        StampCard card = _cards.Has(earned: 5);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _orders.Added!.Items.Count);
        Assert.Equal(3, _orders.Added.UnitsByProduct()[_menu.Gin.Id]);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCardHasMoreThanOneRewardWorth_SpendsOnlyOne()
    {
        _cards.RuleIs(5, _menu.Fernet);
        StampCard card = _cards.Has(earned: 12);

        await AHandler().HandleAsync(ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true }, CancellationToken.None);

        Assert.Equal(1, _orders.Added!.Items.Count(item => item.IsReward));
        Assert.Equal(5, _orders.Added.Items.Single(item => item.IsReward).StampsSpent);
    }

    [Fact]
    public async Task HandleAsync_WhenTheRewardIsAskedWithoutEnoughStamps_RefusesWithoutSpendingOrConfirming()
    {
        _cards.RuleIs(5, _menu.Fernet);
        StampCard card = _cards.Has(earned: 4);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true }, CancellationToken.None);

        Assert.Equal(StampCardErrors.NotEnoughStamps, result.Error);
        Assert.Null(_orders.Added);
        Assert.Equal(0, card.RewardsClaimed);
    }

    [Fact]
    public async Task HandleAsync_WhenTheRewardIsAskedWithoutACard_RefusesAndIssuesNothing()
    {
        _cards.RuleIs(5, _menu.Fernet);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(ATwoGinOrder() with { RedeemReward = true }, CancellationToken.None);

        Assert.Equal(StampCardErrors.NotEnoughStamps, result.Error);
        Assert.Null(_orders.Added);
        Assert.Empty(_cards.Issued);
    }

    [Fact]
    public async Task HandleAsync_WhenTheRewardIsAskedAndTheCardIsOff_RefusesAndConfirmsNothing()
    {
        _cards.RuleIs(5, _menu.Fernet, active: false);
        StampCard card = _cards.Has(earned: 9);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true }, CancellationToken.None);

        Assert.Equal(StampCardErrors.NotActive, result.Error);
        Assert.Null(_orders.Added);
    }

    // US-59, criterion 2: off the menu or switched off.
    [Fact]
    public async Task HandleAsync_WhenTheRewardIsSwitchedOff_RefusesWithoutSpendingOrConfirming()
    {
        _cards.RuleIs(5, _menu.Fernet);
        StampCard card = _cards.Has(earned: 5);
        _menu.Fernet.MarkUnavailable();

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true }, CancellationToken.None);

        Assert.Equal(StampCardErrors.RewardUnavailable, result.Error);
        Assert.Null(_orders.Added);
        Assert.Equal(0, card.RewardsClaimed);
    }

    // US-59, criterion 2: sold out is the order's own refusal, naming the drink.
    [Fact]
    public async Task HandleAsync_WhenTheRewardIsSoldOut_RefusesWithoutConfirming()
    {
        Catalog menu = new(gin: 20, fernet: 0);
        _cards.RuleIs(5, menu.Fernet);
        StampCard card = _cards.Has(earned: 5);

        Result<ConfirmedOrder> result = await AHandlerOver(menu).HandleAsync(
            new ConfirmOrderCommand("María Quadro", PaymentMethod.Digital, "abc-123", [new OrderLineRequest(menu.Gin.Id, 2, null)], StampCardToken: card.Token, RedeemReward: true),
            CancellationToken.None);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Equal("Fernet con Coca", result.Error.Subject);
        Assert.Null(_orders.Added);
    }

    // US-59, criterion 4: the other phone spent the card in the same instant.
    [Fact]
    public async Task HandleAsync_WhenAnotherPhoneSpentTheCardFirst_AnswersThatTheCardMoved()
    {
        _cards.RuleIs(5, _menu.Fernet);
        StampCard card = _cards.Has(earned: 5);
        _orders.RefusesAlways(StampCardErrors.CardMoved);

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true }, CancellationToken.None);

        Assert.Equal(StampCardErrors.CardMoved, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenAnOrderWithRewardIsRetried_HandsBackTheSameOrderAndToken()
    {
        _cards.RuleIs(5, _menu.Fernet);
        StampCard card = _cards.Has(earned: 5);
        ConfirmOrderHandler handler = AHandler();
        ConfirmOrderCommand command = ATwoGinOrder() with { StampCardToken = card.Token, RedeemReward = true };

        Result<ConfirmedOrder> first = await handler.HandleAsync(command, CancellationToken.None);
        Result<ConfirmedOrder> retry = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(first.Value.Code, retry.Value.Code);
        Assert.Equal(card.Token, retry.Value.StampCardToken);
        Assert.Equal(1, card.RewardsClaimed);
    }

    // A fresh card of this order has no stamps to spend, whatever the phone asks.
    [Fact]
    public async Task HandleAsync_WhenTheRewardIsAskedByAPhoneWhoseTokenIsForeign_RefusesWithoutIssuingACard()
    {
        _cards.RuleIs(5, _menu.Fernet);
        string foreign = StampCard.Issue(Guid.CreateVersion7()).Token;

        Result<ConfirmedOrder> result = await AHandler().HandleAsync(
            ATwoGinOrder() with { StampCardToken = foreign, RedeemReward = true }, CancellationToken.None);

        Assert.Equal(StampCardErrors.NotEnoughStamps, result.Error);
        Assert.Empty(_cards.Issued);
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

    private ConfirmOrderHandler AHandlerPaidBy(IPaymentStrategy payment) =>
        new(_orders,
            _menu,
            _menu,
            new SequenceThatAnswers("K-4821"),
            [payment],
            new TheVenueIsFixed(TheVenue),
            _nights,
            new FixedClock(Tonight),
            _promotions,
            _cards,
            _cards);

    private ConfirmOrderHandler AHandlerOver(Catalog menu, IOrderCodeSequence? codes = null, IBarsForOrdering? bars = null) =>
        new(_orders,
            menu,
            bars ?? menu,
            codes ?? new SequenceThatAnswers("K-4821"),
            [new DigitalPaymentStrategy(new FixedClock(Tonight)), new CashPaymentStrategy()],
            new TheVenueIsFixed(TheVenue),
            _nights,
            new FixedClock(Tonight),
            _promotions,
            _cards,
            _cards);

    /// <summary>A method that only takes the money, as every method that pays on the spot does.</summary>
    private sealed class PaysOnTheSpot(DateTimeOffset now) : IPaymentStrategy
    {
        public PaymentMethod Method => PaymentMethod.Digital;

        public PaymentRequest? Request { get; private set; }

        public Task<Error?> SettleAsync(Order order, PaymentRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            order.Pay(now, Method);

            return Task.FromResult<Error?>(null);
        }
    }

    /// <summary>A method that says no, as the table's balance does when it changed under the phone.</summary>
    private sealed class Refuses(Error reason) : IPaymentStrategy
    {
        public PaymentMethod Method => PaymentMethod.Digital;

        public Task<Error?> SettleAsync(Order order, PaymentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<Error?>(reason);
    }

    private sealed class PromotionsOn : IActivePromotions
    {
        private readonly List<Promotion> _promotions = [];

        public void Add(Promotion promotion) => _promotions.Add(promotion);

        public Task<IReadOnlyList<Promotion>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Promotion>>([.. _promotions.Where(promotion => promotion.IsActive)]);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Which night is on, as the handler asks for it; the test can close it.</summary>
    private sealed class TonightIs(Guid night) : IUnderwayNightLookup
    {
        private Guid? _underway = night;

        public DateTimeOffset? AskedAt { get; private set; }

        public void Close() => _underway = null;

        public Task<Guid?> FindIdAsync(DateTimeOffset at, CancellationToken cancellationToken)
        {
            AskedAt = at;

            return Task.FromResult(_underway);
        }
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

        public Task<Order?> GetForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Confirming never loads an existing order.");

        public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Confirming saves through AddAsync.");

        public Task<Result<Order>> SaveCancellationAsync(Order order, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Confirming saves through AddAsync.");

        public Task<Result<Order>> SaveCollectionAsync(Order order, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Confirming saves through AddAsync.");
    }

    /// <summary>
    /// The venue's menu, as the handler is allowed to see it. It holds the real
    /// products, so the ids the tests ask for are the ones the domain generated
    /// and nothing here has to invent one.
    /// </summary>
    private sealed class Catalog(int gin, int fernet) : IProductsForOrdering, IBarsForOrdering
    {
        public Guid MainBar { get; } = Guid.CreateVersion7();

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

        /// <summary>One bar holding the whole stock: what the tests that are not about bars need.</summary>
        public Task<IReadOnlyList<BarSupply>> GetAsync(Guid nightId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BarSupply>>(
                [new BarSupply(MainBar, 0, new Dictionary<Guid, int> { [Gin.Id] = gin, [Fernet.Id] = fernet })]);
    }

    /// <summary>The night's bars, as the tests about choosing one lay them out.</summary>
    private sealed class Bars(params BarSupply[] bars) : IBarsForOrdering
    {
        public Guid? AskedFor { get; private set; }

        public Task<IReadOnlyList<BarSupply>> GetAsync(Guid nightId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
        {
            AskedFor = nightId;

            return Task.FromResult<IReadOnlyList<BarSupply>>(bars);
        }
    }

    /// <summary>The cards and the rule of the venue, as the handler is allowed to see them.</summary>
    private sealed class StampCardsInMemory : IStampCardRepository, IStampCardQueries
    {
        private readonly List<(StampCard Card, StampBalance Balance)> _known = [];

        private StampCardRule? _rule;

        /// <summary>The cards the handler added: they are written with the order.</summary>
        public List<StampCard> Issued { get; } = [];

        public void RuleIs(int stamps, Product reward, bool active = true) =>
            _rule = StampCardRule.Create(TheVenue, stamps, reward.Id, active);

        public StampCard Has(int earned, int spent = 0)
        {
            StampCard card = StampCard.Issue(TheVenue);

            _known.Add((card, new StampBalance(earned, spent)));

            return card;
        }

        public Task<StampCardRule?> GetRuleAsync(CancellationToken cancellationToken) => Task.FromResult(_rule);

        public Task<StampBalance> GetBalanceAsync(Guid cardId, Guid? currentNight, CancellationToken cancellationToken) =>
            Task.FromResult(_known.Single(known => known.Card.Id == cardId).Balance);

        public Task<Guid?> FindCardIdAsync(string? token, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(_known.Select(known => known.Card).Concat(Issued).FirstOrDefault(card => card.Token == token)?.Id);

        public Task<string?> GetTokenAsync(Guid cardId, CancellationToken cancellationToken) =>
            Task.FromResult(_known.Select(known => known.Card).Concat(Issued).FirstOrDefault(card => card.Id == cardId)?.Token);

        public Task<StampCard?> GetForUpdateAsync(string? token, CancellationToken cancellationToken) =>
            Task.FromResult(_known.Select(known => known.Card).FirstOrDefault(card => card.Token == token));

        public void Add(StampCard card) => Issued.Add(card);
    }
}
