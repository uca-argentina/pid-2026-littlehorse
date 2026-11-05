using DrinkIt.Application.Common;

namespace DrinkIt.Application.Promotions;

public static class PromotionErrors
{
    /// <summary>
    /// The venue has no promotion with that id. Another venue's lands here too:
    /// from outside, not ours and not existing are the same answer.
    /// </summary>
    public static readonly Error NotFound =
        new("promotion.not_found", "This venue has no promotion with that id.");

    /// <summary>A product that is not this venue's gets the same answer as one that does not exist.</summary>
    public static readonly Error ProductNotFound =
        new("promotion.product_not_found", "One of the promotion's products is not on this venue's menu.");
}
