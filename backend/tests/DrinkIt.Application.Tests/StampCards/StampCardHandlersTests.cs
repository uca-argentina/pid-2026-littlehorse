using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.StampCards;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.StampCards;

namespace DrinkIt.Application.Tests.StampCards;

/// <summary>US-57 and the reading half of US-58: the rule the administration sets and the card a phone sees.</summary>
public class StampCardHandlersTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private readonly Product _gin = Product.Create(TheVenue, "Gin Tonic", null, null, 4500m, 10, Guid.CreateVersion7());

    private readonly Product _fernet = Product.Create(TheVenue, "Fernet", null, null, 4000m, 10, Guid.CreateVersion7());

    private readonly RuleStore _rules = new();

    private readonly CardStore _cards = new();

    // US-57, criterion 1.
    [Fact]
    public async Task Save_WhenTheVenueHasNoRule_CreatesItActive()
    {
        Result<StampCardRuleSummary> result = await ASaver().HandleAsync(new StampCardRuleDefinition(5, _gin.Id, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TheVenue, _rules.Rule!.VenueId);
        Assert.Equal(5, _rules.Rule.StampsForReward);
        Assert.Equal(_gin.Id, _rules.Rule.RewardProductId);
        Assert.True(_rules.Rule.IsActive);
    }

    // US-57, criterion 2: still one rule, and the cards are not touched.
    [Fact]
    public async Task Save_WhenTheVenueAlreadyHasARule_ReplacesItInsteadOfAddingAnother()
    {
        await ASaver().HandleAsync(new StampCardRuleDefinition(5, _gin.Id, true), CancellationToken.None);
        StampCardRule first = _rules.Rule!;

        await ASaver().HandleAsync(new StampCardRuleDefinition(8, _fernet.Id, true), CancellationToken.None);

        Assert.Same(first, _rules.Rule);
        Assert.Equal(1, _rules.TimesAdded);
        Assert.Equal(8, _rules.Rule!.StampsForReward);
        Assert.Equal(_fernet.Id, _rules.Rule.RewardProductId);
    }

    // US-57, criterion 3.
    [Fact]
    public async Task Save_WhenSwitchedOff_KeepsTheRuleButInactive()
    {
        await ASaver().HandleAsync(new StampCardRuleDefinition(5, _gin.Id, true), CancellationToken.None);

        await ASaver().HandleAsync(new StampCardRuleDefinition(5, _gin.Id, false), CancellationToken.None);

        Assert.False(_rules.Rule!.IsActive);
        Assert.Equal(1, _rules.TimesSaved);
    }

    [Fact]
    public async Task Save_WhenTheRewardIsAnotherVenuesProduct_IsNotFound()
    {
        Result<StampCardRuleSummary> result = await ASaver().HandleAsync(new StampCardRuleDefinition(5, Guid.CreateVersion7(), true), CancellationToken.None);

        Assert.Equal(StampCardErrors.RewardProductNotFound, result.Error);
        Assert.Null(_rules.Rule);
    }

    [Fact]
    public async Task Save_WhenTheRewardIsACombo_IsRefused()
    {
        Product combo = Product.CreateCombo(TheVenue, "Combo", null, null, 9000m, Guid.CreateVersion7(), [(_gin.Id, 2)]);
        RuleStore rules = new();
        SaveStampCardRuleHandler handler = new(rules, new Menu(combo), new Venue());

        Result<StampCardRuleSummary> result = await handler.HandleAsync(new StampCardRuleDefinition(5, combo.Id, true), CancellationToken.None);

        Assert.Equal(StampCardErrors.RewardIsACombo, result.Error);
        Assert.Null(rules.Rule);
    }

    [Fact]
    public async Task Save_WhenFewerThanTwoStamps_BreaksTheDomainRule()
    {
        DomainException error = await Assert.ThrowsAsync<DomainException>(() => ASaver().HandleAsync(new StampCardRuleDefinition(1, _gin.Id, true), CancellationToken.None));

        Assert.Equal(StampCardRule.ErrorCodes.StampsOutOfRange, error.Code);
    }

    // US-57, criterion 3: hidden when off.
    [Fact]
    public async Task Get_WhenTheVenueHasNoRule_IsOff()
    {
        StampCardView view = await AViewer().HandleAsync(null, null, CancellationToken.None);

        Assert.False(view.IsActive);
    }

    [Fact]
    public async Task Get_WhenTheRuleIsSwitchedOff_IsOffEvenForACardWithStamps()
    {
        _cards.Rule = StampCardRule.Create(TheVenue, 5, _gin.Id, false);
        StampCard card = _cards.Has(earned: 4);

        StampCardView view = await AViewer().HandleAsync(card.Token, null, CancellationToken.None);

        Assert.False(view.IsActive);
    }

    [Fact]
    public async Task Get_WhenThePhoneHasNoCardYet_ShowsTheRuleWithZeroStamps()
    {
        _cards.Rule = StampCardRule.Create(TheVenue, 5, _gin.Id, true);

        StampCardView view = await AViewer().HandleAsync(null, null, CancellationToken.None);

        Assert.True(view.IsActive);
        Assert.Equal(5, view.StampsForReward);
        Assert.Equal("Gin Tonic", view.RewardName);
        Assert.Equal(0, view.Stamps);
        Assert.False(view.CanRedeem);
    }

    [Fact]
    public async Task Get_WhenTheTokenIsNobodys_ShowsZeroStampsInsteadOfFailing()
    {
        _cards.Rule = StampCardRule.Create(TheVenue, 5, _gin.Id, true);

        StampCardView view = await AViewer().HandleAsync(StampCard.Issue(Guid.CreateVersion7()).Token, null, CancellationToken.None);

        Assert.Equal(0, view.Stamps);
    }

    [Fact]
    public async Task Get_WhenTheCardIsPartWayThere_ShowsItsStampsAndCannotRedeem()
    {
        _cards.Rule = StampCardRule.Create(TheVenue, 5, _gin.Id, true);
        StampCard card = _cards.Has(earned: 3);

        StampCardView view = await AViewer().HandleAsync(card.Token, null, CancellationToken.None);

        Assert.Equal(3, view.Stamps);
        Assert.False(view.CanRedeem);
    }

    // US-57, criterion 2: the new rule applies to the progress the card already has.
    [Fact]
    public async Task Get_WhenTheRuleIsLoweredBelowWhatTheCardHas_BecomesRedeemable()
    {
        _cards.Rule = StampCardRule.Create(TheVenue, 10, _gin.Id, true);
        StampCard card = _cards.Has(earned: 6);

        Assert.False((await AViewer().HandleAsync(card.Token, null, CancellationToken.None)).CanRedeem);

        _cards.Rule.Update(5, _gin.Id, true);

        StampCardView view = await AViewer().HandleAsync(card.Token, null, CancellationToken.None);

        Assert.True(view.CanRedeem);
        Assert.Equal(5, view.Stamps);
    }

    [Fact]
    public async Task Get_WhenTheCardSpentStamps_ShowsOnlyWhatIsLeft()
    {
        _cards.Rule = StampCardRule.Create(TheVenue, 5, _gin.Id, true);
        StampCard card = _cards.Has(earned: 7, spent: 5);

        StampCardView view = await AViewer().HandleAsync(card.Token, null, CancellationToken.None);

        Assert.Equal(2, view.Stamps);
        Assert.False(view.CanRedeem);
    }

    [Fact]
    public async Task Get_WhenAnUndoneDeliveryLeavesTheBalanceBelowZero_ShowsZero()
    {
        _cards.Rule = StampCardRule.Create(TheVenue, 5, _gin.Id, true);
        StampCard card = _cards.Has(earned: 4, spent: 5);

        Assert.Equal(0, (await AViewer().HandleAsync(card.Token, null, CancellationToken.None)).Stamps);
    }

    [Fact]
    public async Task Get_WhenTheCardIsComplete_CanRedeemAndShowsTheFullCard()
    {
        _cards.Rule = StampCardRule.Create(TheVenue, 5, _gin.Id, true);
        StampCard card = _cards.Has(earned: 12);

        StampCardView view = await AViewer().HandleAsync(card.Token, null, CancellationToken.None);

        Assert.Equal(5, view.Stamps);
        Assert.True(view.CanRedeem);
    }

    private SaveStampCardRuleHandler ASaver() => new(_rules, new Menu(_gin, _fernet), new Venue());

    private GetStampCardHandler AViewer() => new(_cards, new Menu(_gin, _fernet));

    private sealed class Venue : ICurrentVenue
    {
        public Guid Id => TheVenue;
    }

    private sealed class Menu(params Product[] products) : IProductsForOrdering
    {
        public Task<IReadOnlyList<Product>> GetForOrderingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Product>>([.. products.Where(product => ids.Contains(product.Id))]);
    }

    private sealed class RuleStore : IStampCardRuleRepository
    {
        public StampCardRule? Rule { get; private set; }

        public int TimesAdded { get; private set; }

        public int TimesSaved { get; private set; }

        public Task<StampCardRule?> GetForUpdateAsync(CancellationToken cancellationToken) => Task.FromResult(Rule);

        public Task AddAsync(StampCardRule rule, CancellationToken cancellationToken)
        {
            Rule = rule;
            TimesAdded++;

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            TimesSaved++;

            return Task.CompletedTask;
        }
    }

    private sealed class CardStore : IStampCardQueries
    {
        private readonly List<(StampCard Card, StampBalance Balance)> _known = [];

        public StampCardRule? Rule { get; set; }

        public StampCard Has(int earned, int spent = 0)
        {
            StampCard card = StampCard.Issue(TheVenue);

            _known.Add((card, new StampBalance(earned, spent)));

            return card;
        }

        public Task<StampCardRule?> GetRuleAsync(CancellationToken cancellationToken) => Task.FromResult(Rule);

        public Task<StampBalance> GetBalanceAsync(Guid cardId, Guid? currentNight, CancellationToken cancellationToken) =>
            Task.FromResult(_known.Single(known => known.Card.Id == cardId).Balance);

        public Task<Guid?> FindCardIdAsync(string? token, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(_known.FirstOrDefault(known => known.Card.Token == token).Card?.Id);

        public Task<string?> GetTokenAsync(Guid cardId, CancellationToken cancellationToken) =>
            Task.FromResult(_known.FirstOrDefault(known => known.Card.Id == cardId).Card?.Token);
    }
}
