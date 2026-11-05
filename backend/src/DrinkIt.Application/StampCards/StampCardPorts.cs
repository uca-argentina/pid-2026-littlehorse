using DrinkIt.Application.Common;
using DrinkIt.Domain.StampCards;

namespace DrinkIt.Application.StampCards;

/// <summary>
/// What a card holds, derived from orders and never stored (US-58): the units
/// of its delivered orders, and the stamps its rewards spent.
/// </summary>
public sealed record StampBalance(int Earned, int Spent)
{
    /// <summary>Undoing a delivery after spending can leave it below zero for a moment; it never shows as negative.</summary>
    public int Available => Math.Max(0, Earned - Spent);
}

/// <summary>The write side of a card: loaded to claim a reward (US-59), added when the first order issues it.</summary>
public interface IStampCardRepository
{
    /// <summary>
    /// The card with that token, tracked. Null when the token is malformed,
    /// unknown or another venue's: the venue filter makes those one answer.
    /// </summary>
    Task<StampCard?> GetForUpdateAsync(string? token, CancellationToken cancellationToken);

    /// <summary>Written by the same save as the order that issues it.</summary>
    void Add(StampCard card);
}

/// <summary>The read side: the rule, the balance of a card, and whose token is whose.</summary>
public interface IStampCardQueries
{
    /// <summary>The venue's rule, or null when nobody has set one up: without it there is no card.</summary>
    Task<StampCardRule?> GetRuleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stamps earned minus stamps spent. A cash order of a night that is over and
    /// was never collected holds nothing (backlog note of US-59).
    /// </summary>
    Task<StampBalance> GetBalanceAsync(Guid cardId, Guid? currentNight, CancellationToken cancellationToken);

    /// <summary>The id of the card with that token in this venue, or null.</summary>
    Task<Guid?> FindCardIdAsync(string? token, CancellationToken cancellationToken);

    Task<string?> GetTokenAsync(Guid cardId, CancellationToken cancellationToken);
}

/// <summary>The write side of the venue's rule (US-57). One per venue.</summary>
public interface IStampCardRuleRepository
{
    Task<StampCardRule?> GetForUpdateAsync(CancellationToken cancellationToken);

    Task AddAsync(StampCardRule rule, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public static class StampCardErrors
{
    /// <summary>The venue has no card, or has switched it off: nothing is earned or redeemed.</summary>
    public static readonly Error NotActive =
        new("stamp_card.not_active", "The venue has no stamp card right now.");

    public static readonly Error NotEnoughStamps =
        new("stamp_card.not_enough_stamps", "The card does not have enough stamps for the reward.");

    /// <summary>The reward is off the menu or switched off for tonight. Sold out is the order's own error.</summary>
    public static readonly Error RewardUnavailable =
        new("stamp_card.reward_unavailable", "The reward is not available right now.");

    /// <summary>Another phone spent the card first. Nothing was saved; the card is read again.</summary>
    public static readonly Error CardMoved =
        new("stamp_card.card_moved", "The card changed while the reward was being claimed.");

    public static readonly Error RewardProductNotFound =
        new("stamp_card.reward_product_not_found", "The reward is not on this venue's menu.");

    public static readonly Error RewardIsACombo =
        new("stamp_card.reward_is_a_combo", "A combo cannot be the reward.");
}
