using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.StampCards;

namespace DrinkIt.Application.StampCards;

/// <summary>What the administration sends (US-57).</summary>
public sealed record StampCardRuleDefinition(int StampsForReward, Guid RewardProductId, bool IsActive);

public sealed record StampCardRuleSummary(
    int StampsForReward,
    Guid RewardProductId,
    bool IsActive,
    AuditInfo Audit)
{
    public static StampCardRuleSummary Of(StampCardRule rule) => new(
        rule.StampsForReward,
        rule.RewardProductId,
        rule.IsActive,
        new AuditInfo(rule.CreatedAt, rule.CreatedBy, rule.LastModifiedAt, rule.LastModifiedBy));
}

/// <summary>US-57: sets up the venue's card, or replaces the rule it has. Changing it keeps every customer's stamps.</summary>
public sealed class SaveStampCardRuleHandler(
    IStampCardRuleRepository rules,
    IProductsForOrdering products,
    ICurrentVenue currentVenue)
{
    public async Task<Result<StampCardRuleSummary>> HandleAsync(StampCardRuleDefinition command, CancellationToken cancellationToken)
    {
        // The venue filter makes another venue's product not found, which is
        // the answer for both. The domain refuses the rest with its own codes,
        // before asking for any product when there is none to ask for.
        if (command.RewardProductId != Guid.Empty)
        {
            Product? reward = (await products.GetForOrderingAsync([command.RewardProductId], cancellationToken)).FirstOrDefault(found => found.Id == command.RewardProductId);

            if (reward is null) return StampCardErrors.RewardProductNotFound;
            if (reward.IsCombo) return StampCardErrors.RewardIsACombo;
        }

        StampCardRule? rule = await rules.GetForUpdateAsync(cancellationToken);

        if (rule is null)
        {
            rule = StampCardRule.Create(currentVenue.Id, command.StampsForReward, command.RewardProductId, command.IsActive);
            await rules.AddAsync(rule, cancellationToken);
        }
        else
        {
            rule.Update(command.StampsForReward, command.RewardProductId, command.IsActive);
            await rules.SaveChangesAsync(cancellationToken);
        }

        return StampCardRuleSummary.Of(rule);
    }
}

/// <summary>
/// The card as the phone shows it (US-58). <see cref="Stamps"/> is the progress
/// toward the next reward, capped at what it takes; <see cref="CanRedeem"/> says
/// the reward can be added to an order.
/// </summary>
public sealed record StampCardView(
    bool IsActive,
    int StampsForReward,
    Guid? RewardProductId,
    string? RewardName,
    int Stamps,
    bool CanRedeem);

/// <summary>
/// What a phone reads about its card: public about the rule (it is on the wall
/// of the venue), private about the stamps, which only the token opens.
/// </summary>
public sealed class GetStampCardHandler(IStampCardQueries cards, IProductsForOrdering products)
{
    public static readonly StampCardView Off = new(false, 0, null, null, 0, false);

    public async Task<StampCardView> HandleAsync(string? token, Guid? currentNight, CancellationToken cancellationToken)
    {
        StampCardRule? rule = await cards.GetRuleAsync(cancellationToken);

        if (rule is not { IsActive: true }) return Off;

        Product? reward = (await products.GetForOrderingAsync([rule.RewardProductId], cancellationToken)).FirstOrDefault(found => found.Id == rule.RewardProductId);

        // A token nobody has — wrong, from another venue — is a card with no
        // stamps, not an error: the next order issues the phone a fresh one.
        Guid? cardId = await cards.FindCardIdAsync(token, cancellationToken);
        StampBalance balance = cardId is Guid id ? await cards.GetBalanceAsync(id, currentNight, cancellationToken) : new StampBalance(0, 0);

        return new StampCardView(
            true,
            rule.StampsForReward,
            rule.RewardProductId,
            reward?.Name,
            Math.Min(balance.Available, rule.StampsForReward),
            balance.Available >= rule.StampsForReward);
    }
}
