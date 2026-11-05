using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.StampCards;

/// <summary>
/// What the venue promises (US-57): this many stamps earn this product. One per
/// venue; without it there is no card. Switching it off hides the card and keeps
/// every customer's stamps.
/// </summary>
public sealed class StampCardRule : AuditStamps, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "stamp_card_rule.venue_required";
        public const string StampsOutOfRange = "stamp_card_rule.stamps_out_of_range";
        public const string RewardRequired = "stamp_card_rule.reward_required";
    }

    public const int MinStampsForReward = 2;

    public const int MaxStampsForReward = 50;

    private StampCardRule(Guid id, Guid venueId)
    {
        Id = id;
        VenueId = venueId;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public int StampsForReward { get; private set; }

    public Guid RewardProductId { get; private set; }

    public bool IsActive { get; private set; }

    public static StampCardRule Create(Guid venueId, int stampsForReward, Guid rewardProductId, bool isActive)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "The stamp card rule must belong to a venue.");

        StampCardRule rule = new(Guid.CreateVersion7(), venueId);

        rule.Update(stampsForReward, rewardProductId, isActive);

        return rule;
    }

    /// <summary>
    /// Replaces the rule. Validates it all first, so a refusal changes nothing.
    /// Cards keep their stamps: they are counted from orders, not stored here.
    /// </summary>
    public void Update(int stampsForReward, Guid rewardProductId, bool isActive)
    {
        if (stampsForReward is < MinStampsForReward or > MaxStampsForReward) throw new DomainException(ErrorCodes.StampsOutOfRange, $"The reward takes between {MinStampsForReward} and {MaxStampsForReward} stamps.");
        if (rewardProductId == Guid.Empty) throw new DomainException(ErrorCodes.RewardRequired, "The card needs a product to give.");

        StampsForReward = stampsForReward;
        RewardProductId = rewardProductId;
        IsActive = isActive;
    }
}
