using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Metrics;
using DrinkIt.Application.Orders;
using DrinkIt.Application.StampCards;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.StampCards;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Metrics;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Promotions;
using DrinkIt.Infrastructure.StampCards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// US-57 to US-59 against a real database: stamps derived from delivered
/// orders, the reward as a free line that takes stock like any other, the card
/// as a per-venue secret, and two phones spending one card.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class StampCardTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Tonight = new(2026, 9, 17, 2, 30, 0, TimeSpan.Zero);

    // US-58, criterion 1: the first order is given a card, kept in the database.
    [Fact]
    public async Task Confirm_WhenTheCardIsOnAndThePhoneHasNone_IssuesACardWithAToken()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);

        Result<ConfirmedOrder> result = await Confirm(seeded, [Gin(2)]);

        Assert.True(result.IsSuccess);
        Assert.True(StampCard.IsWellShaped(result.Value.StampCardToken));
        Assert.NotNull(await CardIdOf(seeded, result.Value.StampCardToken));
    }

    // US-58, criterion 1, "las próximas veces viene sola".
    [Fact]
    public async Task Confirm_WhenThePhoneBringsItsToken_KeepsTheSameCard()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);
        string token = (await Confirm(seeded, [Gin(1)], key: "first")).Value.StampCardToken!;

        Result<ConfirmedOrder> second = await Confirm(seeded, [Gin(1)], key: "second", token: token);

        Assert.Equal(token, second.Value.StampCardToken);
        Assert.Equal(1, await CountCards(seeded));
    }

    // US-58, criterion 2: three units delivered, three stamps; not before.
    [Fact]
    public async Task Balance_WhenAnOrderOfThreeIsDelivered_IsThree()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);
        Result<ConfirmedOrder> order = await Confirm(seeded, [Gin(3)]);

        Assert.Equal(0, (await BalanceOf(seeded, order.Value.StampCardToken)).Available);

        await Deliver(seeded, order.Value.Code);

        Assert.Equal(3, (await BalanceOf(seeded, order.Value.StampCardToken)).Available);
    }

    [Fact]
    public async Task Balance_WhenOrdersOfTheSameCardAreDelivered_AddsThemUp()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);
        Result<ConfirmedOrder> first = await Confirm(seeded, [Gin(3)], key: "one");
        Result<ConfirmedOrder> second = await Confirm(seeded, [Gin(2)], key: "two", token: first.Value.StampCardToken);

        await Deliver(seeded, first.Value.Code);
        await Deliver(seeded, second.Value.Code);

        Assert.Equal(5, (await BalanceOf(seeded, first.Value.StampCardToken)).Available);
    }

    // US-58 note: a combo gives one stamp per combo, not per component.
    [Fact]
    public async Task Balance_WhenACombosDelivered_CountsOnePerComboUnit()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);
        Result<ConfirmedOrder> order = await Confirm(seeded, [new OrderLineRequest(seeded.Combo.Id, 2, null)]);

        await Deliver(seeded, order.Value.Code);

        Assert.Equal(2, (await BalanceOf(seeded, order.Value.StampCardToken)).Available);
    }

    // US-58, criterion 3: delivery undone.
    [Fact]
    public async Task Balance_WhenTheDeliveryIsUndone_GoesBackWithoutAnyCompensation()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);
        Result<ConfirmedOrder> order = await Confirm(seeded, [Gin(3)]);
        await Deliver(seeded, order.Value.Code);

        await using (DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id))
        {
            OrderRepository repository = new(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance);
            Order loaded = (await repository.GetForUpdateAsync(OrderCode.Parse(order.Value.Code), CancellationToken.None))!;

            loaded.UndoDelivery(Tonight);
            Assert.True((await repository.SaveAsync(loaded, CancellationToken.None)).IsSuccess);
        }

        Assert.Equal(0, (await BalanceOf(seeded, order.Value.StampCardToken)).Available);
    }

    // US-58, criterion 3: canceled, or never paid.
    [Fact]
    public async Task Balance_WhenAnOrderIsCanceledOrNeverPaid_GivesNothing()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);
        Result<ConfirmedOrder> unpaid = await Confirm(seeded, [Gin(2)], PaymentMethod.Cash, key: "unpaid");
        Result<ConfirmedOrder> canceled = await Confirm(seeded, [Gin(2)], PaymentMethod.Cash, key: "canceled", token: unpaid.Value.StampCardToken);

        await Cancel(seeded, canceled.Value.Code);

        Assert.Equal(0, (await BalanceOf(seeded, unpaid.Value.StampCardToken)).Available);
    }

    // US-58, criterion 4, and the most important invariant of the model.
    [Fact]
    public async Task Confirm_WhenTheTokenIsOfAnotherVenue_EarnsNothingThereAndGetsACardOfThisVenue()
    {
        Seeded mine = await Seed();
        Seeded theirs = await Seed();
        await SetRule(mine, 5, mine.Fernet);
        await SetRule(theirs, 5, theirs.Fernet);
        string theirToken = (await Confirm(theirs, [Gin(1)], key: "theirs")).Value.StampCardToken!;

        Result<ConfirmedOrder> order = await Confirm(mine, [Gin(2)], token: theirToken);
        await Deliver(mine, order.Value.Code);

        Assert.NotEqual(theirToken, order.Value.StampCardToken);
        Assert.Null(await CardIdOf(mine, theirToken));
        Assert.Equal(2, (await BalanceOf(mine, order.Value.StampCardToken)).Available);
        Assert.Equal(0, (await BalanceOf(theirs, theirToken)).Available);
    }

    [Fact]
    public async Task View_WhenTheTokenIsOfAnotherVenue_ShowsAnEmptyCard()
    {
        Seeded mine = await Seed();
        Seeded theirs = await Seed();
        await SetRule(mine, 5, mine.Fernet);
        await SetRule(theirs, 5, theirs.Fernet);
        Result<ConfirmedOrder> theirOrder = await Confirm(theirs, [Gin(3)]);
        await Deliver(theirs, theirOrder.Value.Code);

        StampCardView view = await View(mine, theirOrder.Value.StampCardToken);

        Assert.True(view.IsActive);
        Assert.Equal(0, view.Stamps);
    }

    // US-57: one rule per venue, and another venue's is not visible.
    [Fact]
    public async Task Rule_WhenAnotherVenueSetsOneUp_IsNotVisibleFromThisOne()
    {
        Seeded mine = await Seed();
        Seeded theirs = await Seed();
        await SetRule(theirs, 5, theirs.Fernet);

        await using DrinkItDbContext context = sql.CreateContext(mine.Venue.Id);

        Assert.Null(await new StampCardQueries(context).GetRuleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rule_WhenASecondOneIsWrittenForTheSameVenue_IsRefusedByTheDatabase()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        context.StampCardRules.Add(StampCardRule.Create(seeded.Venue.Id, 4, seeded.Gin.Id, true));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // US-57, criterion 2: stamps stay, the new rule applies.
    [Fact]
    public async Task Rule_WhenChanged_KeepsTheStampsAndAppliesToThem()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 10, seeded.Fernet);
        Result<ConfirmedOrder> order = await Confirm(seeded, [Gin(4)]);
        await Deliver(seeded, order.Value.Code);

        Assert.False((await View(seeded, order.Value.StampCardToken)).CanRedeem);

        await SetRule(seeded, 3, seeded.Gin);

        StampCardView view = await View(seeded, order.Value.StampCardToken);

        Assert.True(view.CanRedeem);
        Assert.Equal("Gin Tonic", view.RewardName);
    }

    // US-57, criterion 3: off, nothing is earned or redeemed; on again, stamps are back.
    [Fact]
    public async Task Rule_WhenSwitchedOff_HidesTheCardEarnsNothingAndKeepsTheStamps()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);
        Result<ConfirmedOrder> before = await Confirm(seeded, [Gin(2)], key: "before");
        await Deliver(seeded, before.Value.Code);
        string token = before.Value.StampCardToken!;

        await SetRule(seeded, 5, seeded.Fernet, active: false);

        Result<ConfirmedOrder> during = await Confirm(seeded, [Gin(2)], key: "during", token: token);
        await Deliver(seeded, during.Value.Code);
        Result<ConfirmedOrder> redeeming = await Confirm(seeded, [Gin(1)], key: "redeeming", token: token, redeem: true);

        Assert.False((await View(seeded, token)).IsActive);
        Assert.Null(during.Value.StampCardToken);
        Assert.Equal(StampCardErrors.NotActive, redeeming.Error);

        await SetRule(seeded, 5, seeded.Fernet);

        StampCardView back = await View(seeded, token);

        Assert.True(back.IsActive);
        Assert.Equal(2, back.Stamps);
    }

    // US-59, criterion 1, and the note: the same product, paid and free, in one order.
    [Fact]
    public async Task Redeem_WhenTheCardIsComplete_AddsTheFreeLineAndTakesBothFromTheStock()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Gin);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);

        Result<ConfirmedOrder> redeemed = await Confirm(seeded, [Gin(1)], key: "redeem", token: earned.Value.StampCardToken, redeem: true);

        Assert.True(redeemed.IsSuccess);
        Assert.Equal(4500m, redeemed.Value.Total);
        Assert.Equal(16, await UnitsAt(seeded, seeded.Gin));
        Assert.Equal(16, await GeneralStock(seeded, seeded.Gin));
        Assert.Equal(0, (await BalanceOf(seeded, earned.Value.StampCardToken)).Available);

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        Order stored = await context.Orders.Include(order => order.Items).SingleAsync(order => order.Id == redeemed.Value.Id);

        Assert.Equal(2, stored.Items.Count);
        Assert.Equal(0m, stored.Items.Single(item => item.IsReward).UnitPrice);
        Assert.Equal(2, stored.Items.Single(item => item.IsReward).StampsSpent);
    }

    [Fact]
    public async Task Redeem_WhenTheCardHasNotEnough_RefusesAndWritesNothing()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 5, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);

        Result<ConfirmedOrder> redeemed = await Confirm(seeded, [Gin(1)], key: "redeem", token: earned.Value.StampCardToken, redeem: true);

        Assert.Equal(StampCardErrors.NotEnoughStamps, redeemed.Error);
        Assert.Equal(18, await UnitsAt(seeded, seeded.Gin));
        Assert.Equal(20, await UnitsAt(seeded, seeded.Fernet));
        Assert.Equal(1, await CountOrders(seeded));
    }

    // US-59, criterion 2: sold out, and nothing is spent.
    [Fact]
    public async Task Redeem_WhenTheRewardIsSoldOut_RefusesWithoutSpendingStamps()
    {
        Seeded seeded = await Seed(fernet: 0);
        await SetRule(seeded, 2, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);

        Result<ConfirmedOrder> redeemed = await Confirm(seeded, [Gin(1)], key: "redeem", token: earned.Value.StampCardToken, redeem: true);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, redeemed.Error!.Code);
        Assert.Equal(2, (await BalanceOf(seeded, earned.Value.StampCardToken)).Available);
        Assert.Equal(0, await RewardsClaimed(seeded, earned.Value.StampCardToken));
        Assert.Equal(1, await CountOrders(seeded));
    }

    // US-59, criterion 2: switched off.
    [Fact]
    public async Task Redeem_WhenTheRewardIsSwitchedOff_RefusesWithoutSpendingStamps()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);
        await using (DrinkItDbContext write = sql.CreateContext(seeded.Venue.Id))
        {
            (await write.Products.SingleAsync(product => product.Id == seeded.Fernet.Id)).MarkUnavailable();
            await write.SaveChangesAsync();
        }

        Result<ConfirmedOrder> redeemed = await Confirm(seeded, [Gin(1)], key: "redeem", token: earned.Value.StampCardToken, redeem: true);

        Assert.Equal(StampCardErrors.RewardUnavailable, redeemed.Error);
        Assert.Equal(2, (await BalanceOf(seeded, earned.Value.StampCardToken)).Available);
        Assert.Equal(0, await RewardsClaimed(seeded, earned.Value.StampCardToken));
    }

    // US-59, criterion 3: a cash order with a reward holds the stamps until it is canceled.
    [Fact]
    public async Task Redeem_WhenACashOrderWithTheRewardIsCanceled_ReturnsTheStamps()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);
        Result<ConfirmedOrder> cash = await Confirm(seeded, [Gin(1)], PaymentMethod.Cash, key: "cash", token: earned.Value.StampCardToken, redeem: true);

        Assert.True(cash.IsSuccess);
        Assert.Equal(0, (await BalanceOf(seeded, earned.Value.StampCardToken)).Available);

        await Cancel(seeded, cash.Value.Code);

        Assert.Equal(2, (await BalanceOf(seeded, earned.Value.StampCardToken)).Available);
    }

    // Backlog note of US-59: cash nobody collected from a night that is over holds nothing.
    [Fact]
    public async Task Balance_WhenTheCashOrderWithTheRewardIsFromAPastNight_DoesNotHoldTheStamps()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);
        await Confirm(seeded, [Gin(1)], PaymentMethod.Cash, key: "cash", token: earned.Value.StampCardToken, redeem: true);

        Assert.Equal(0, (await BalanceOf(seeded, earned.Value.StampCardToken, seeded.Night.Id)).Available);
        Assert.Equal(2, (await BalanceOf(seeded, earned.Value.StampCardToken, Guid.CreateVersion7())).Available);
    }

    [Fact]
    public async Task Redeem_WhenTheSameRequestIsRetried_SpendsTheStampsOnce()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(4)], key: "earn");
        await Deliver(seeded, earned.Value.Code);

        Result<ConfirmedOrder> first = await Confirm(seeded, [Gin(1)], key: "redeem", token: earned.Value.StampCardToken, redeem: true);
        Result<ConfirmedOrder> retry = await Confirm(seeded, [Gin(1)], key: "redeem", token: earned.Value.StampCardToken, redeem: true);

        Assert.Equal(first.Value.Code, retry.Value.Code);
        Assert.Equal(2, (await BalanceOf(seeded, earned.Value.StampCardToken)).Available);
    }

    // US-59, criterion 4: two phones, stamps for one reward.
    [Fact]
    public async Task Redeem_WhenTwoPhonesClaimTheSameRewardAtOnce_OnlyOneSucceeds()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);
        string token = earned.Value.StampCardToken!;

        Result<ConfirmedOrder>[] results = await Task.WhenAll(
            Confirm(seeded, [Gin(1)], key: "phone-1", token: token, redeem: true),
            Confirm(seeded, [Gin(1)], key: "phone-2", token: token, redeem: true));

        Result<ConfirmedOrder> loser = Assert.Single(results, result => !result.IsSuccess);

        Assert.Single(results, result => result.IsSuccess);
        Assert.Contains(loser.Error!.Code, new[] { StampCardErrors.CardMoved.Code, StampCardErrors.NotEnoughStamps.Code });
        Assert.Equal(1, await RewardsClaimed(seeded, token));
        Assert.Equal(0, (await BalanceOf(seeded, token)).Available);
        Assert.Equal(19, await UnitsAt(seeded, seeded.Fernet));
    }

    // The same race, forced: both phones read the card before either writes.
    [Fact]
    public async Task AddAsync_WhenAnotherPhoneClaimedTheCardAfterItWasRead_AnswersThatTheCardMovedAndWritesNothing()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);
        string token = earned.Value.StampCardToken!;

        await using DrinkItDbContext first = sql.CreateContext(seeded.Venue.Id);
        await using DrinkItDbContext second = sql.CreateContext(seeded.Venue.Id);
        StampCard firstCard = (await new StampCardRepository(first).GetForUpdateAsync(token, CancellationToken.None))!;
        StampCard secondCard = (await new StampCardRepository(second).GetForUpdateAsync(token, CancellationToken.None))!;

        Result<Order> won = await new OrderRepository(first, new NoDispatcher(), NullLogger<OrderRepository>.Instance)
            .AddAsync(AnOrderClaiming(seeded, firstCard, "K-4821"), "first", CancellationToken.None);
        Result<Order> lost = await new OrderRepository(second, new NoDispatcher(), NullLogger<OrderRepository>.Instance)
            .AddAsync(AnOrderClaiming(seeded, secondCard, "K-4822"), "second", CancellationToken.None);

        Assert.True(won.IsSuccess);
        Assert.Equal(StampCardErrors.CardMoved, lost.Error);
        Assert.Equal(2, await CountOrders(seeded));
    }

    // US-59 note: the reward shows at zero in sales and counts as a unit.
    [Fact]
    public async Task Sales_WhenARewardWasGiven_CountsItsUnitAtZero()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Gin);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);
        await Confirm(seeded, [Gin(1)], key: "redeem", token: earned.Value.StampCardToken, redeem: true);

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        NightSales sales = (await new NightMetricsQueries(context).GetAsync(seeded.Night.Id, CancellationToken.None))!;
        ProductSales gin = sales.Products.Single(product => product.ProductId == seeded.Gin.Id);

        Assert.Equal(4, gin.Units);
        Assert.Equal(13500m, gin.Billed);
    }

    // US-59 note, and the tracking screen: the reward is told apart from a paid line.
    [Fact]
    public async Task Tracking_WhenTheOrderHasAReward_MarksItsLine()
    {
        Seeded seeded = await Seed();
        await SetRule(seeded, 2, seeded.Fernet);
        Result<ConfirmedOrder> earned = await Confirm(seeded, [Gin(2)], key: "earn");
        await Deliver(seeded, earned.Value.Code);
        Result<ConfirmedOrder> redeemed = await Confirm(seeded, [Gin(1)], key: "redeem", token: earned.Value.StampCardToken, redeem: true);

        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        TrackedOrder found = (await new OrderTrackingQueries(context).FindAsync(redeemed.Value.Code, redeemed.Value.TrackingToken, CancellationToken.None))!;

        Assert.Equal("Fernet", found.Items.Single(item => item.IsReward).ProductName);
        Assert.False(found.Items.Single(item => item.ProductName == "Gin Tonic").IsReward);
        Assert.Equal(4500m, found.Total);
    }

    private static OrderLineRequest Gin(int quantity) => new(Guid.Empty, quantity, null);

    private static Order AnOrderClaiming(Seeded seeded, StampCard card, string code)
    {
        Order order = Order.Place(
            seeded.Venue.Id,
            seeded.Night.Id,
            "María Quadro",
            OrderCode.Parse(code),
            [new NewOrderItem(seeded.Gin.Id, "Gin Tonic", 4500m, 1, null), NewOrderItem.Reward(seeded.Fernet.Id, "Fernet", 2)],
            card.Id);

        card.Claim();
        order.AwaitPayment(PaymentMethod.Cash);

        return order;
    }

    private async Task SetRule(Seeded seeded, int stamps, Product reward, bool active = true)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        Result<StampCardRuleSummary> result = await new SaveStampCardRuleHandler(
            new StampCardRuleRepository(context),
            new ProductsForOrdering(context),
            new FixedVenue(seeded.Venue.Id))
            .HandleAsync(new StampCardRuleDefinition(stamps, reward.Id, active), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A line of the gin tonic: the seeded product's id is not known to the static helper, so it is filled in here.</summary>
    private async Task<Result<ConfirmedOrder>> Confirm(
        Seeded seeded,
        OrderLineRequest[] lines,
        PaymentMethod method = PaymentMethod.Digital,
        string key = "a-key",
        string? token = null,
        bool redeem = false)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        FixedVenue current = new(seeded.Venue.Id);
        OrderLineRequest[] real = [.. lines.Select(line => line.ProductId == Guid.Empty ? line with { ProductId = seeded.Gin.Id } : line)];

        return await new ConfirmOrderHandler(
            new OrderRepository(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance),
            new ProductsForOrdering(context),
            new BarsForOrdering(context),
            new OrderCodeSequence(context, current),
            [new DigitalPaymentStrategy(new FixedClock(Tonight)), new CashPaymentStrategy()],
            current,
            new UnderwayNightLookup(context),
            new FixedClock(Tonight),
            new ActivePromotions(context),
            new StampCardRepository(context),
            new StampCardQueries(context))
            .HandleAsync(
                new ConfirmOrderCommand("María Quadro", method, key, real, StampCardToken: token, RedeemReward: redeem),
                CancellationToken.None);
    }

    private async Task Deliver(Seeded seeded, string code)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        OrderRepository repository = new(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance);
        Order order = (await repository.GetForUpdateAsync(OrderCode.Parse(code), CancellationToken.None))!;

        order.StartPreparing(Tonight);
        order.MarkReady(Tonight);
        order.Deliver(Tonight);

        Assert.True((await repository.SaveAsync(order, CancellationToken.None)).IsSuccess);
    }

    private async Task Cancel(Seeded seeded, string code)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        OrderRepository repository = new(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance);
        Order order = (await repository.GetForUpdateAsync(OrderCode.Parse(code), CancellationToken.None))!;

        order.Cancel();

        Assert.True((await repository.SaveCancellationAsync(order, CancellationToken.None)).IsSuccess);
    }

    private async Task<StampBalance> BalanceOf(Seeded seeded, string? token, Guid? night = null)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        StampCardQueries queries = new(context);
        Guid id = (await queries.FindCardIdAsync(token, CancellationToken.None))!.Value;

        return await queries.GetBalanceAsync(id, night ?? seeded.Night.Id, CancellationToken.None);
    }

    private async Task<Guid?> CardIdOf(Seeded seeded, string? token)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await new StampCardQueries(context).FindCardIdAsync(token, CancellationToken.None);
    }

    private async Task<StampCardView> View(Seeded seeded, string? token)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await new GetStampCardHandler(new StampCardQueries(context), new ProductsForOrdering(context))
            .HandleAsync(token, seeded.Night.Id, CancellationToken.None);
    }

    private async Task<int> RewardsClaimed(Seeded seeded, string? token)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await context.StampCards.Where(card => card.Token == token).Select(card => card.RewardsClaimed).SingleAsync();
    }

    private async Task<int> CountCards(Seeded seeded)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await context.StampCards.CountAsync();
    }

    private async Task<int> CountOrders(Seeded seeded)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await context.Orders.CountAsync();
    }

    private async Task<int> UnitsAt(Seeded seeded, Product product)
    {
        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);

        return await read.BarStocks
            .Where(stock => stock.NightId == seeded.Night.Id && stock.KdsId == seeded.MainBar.Id && stock.ProductId == product.Id)
            .Select(stock => stock.Units)
            .SingleAsync();
    }

    private async Task<int> GeneralStock(Seeded seeded, Product product)
    {
        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);

        return await read.Products.Where(row => row.Id == product.Id).Select(row => row.Stock).SingleAsync();
    }

    private async Task<Seeded> Seed(int gin = 20, int fernet = 20)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product ginProduct = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, gin, category.Id);
        Product fernetProduct = Product.Create(venue.Id, "Fernet", null, null, 4000m, fernet, category.Id);
        Product combo = Product.CreateCombo(venue.Id, "Combo Gin", null, null, 8000m, category.Id, [(ginProduct.Id, 1), (fernetProduct.Id, 1)]);
        StaffUser bar = StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier);
        Night night = Night.Create(venue.Id, "Tonight", Tonight.AddHours(-3), Tonight.AddHours(4), [bar, till]);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.AddRange(ginProduct, fernetProduct);
        await seed.SaveChangesAsync();
        seed.Products.Add(combo);
        seed.StaffUsers.AddRange(bar, till);
        seed.Nights.Add(night);
        seed.BarStocks.AddRange(
            new BarStock { VenueId = venue.Id, NightId = night.Id, KdsId = bar.Id, ProductId = ginProduct.Id, Units = gin },
            new BarStock { VenueId = venue.Id, NightId = night.Id, KdsId = bar.Id, ProductId = fernetProduct.Id, Units = fernet });
        await seed.SaveChangesAsync();

        return new Seeded(venue, night, bar, ginProduct, fernetProduct, combo);
    }

    private sealed record Seeded(Venue Venue, Night Night, StaffUser MainBar, Product Gin, Product Fernet, Product Combo);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id { get; } = id;
    }

    private sealed class NoDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
