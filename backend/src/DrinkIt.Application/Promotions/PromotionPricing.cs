using DrinkIt.Application.Common;
using DrinkIt.Domain.Promotions;

namespace DrinkIt.Application.Promotions;

/// <summary>What a product costs right now: the price to pay, and what a promotion took off it, if one did.</summary>
/// <param name="Price">What the customer pays. The list price when no promotion applies.</param>
/// <param name="RegularPrice">The list price, only when a promotion lowered <paramref name="Price"/>.</param>
/// <param name="PromotionName">The promotion that lowered it.</param>
public sealed record PricedProduct(decimal Price, decimal? RegularPrice, string? PromotionName);

/// <summary>
/// The one rule that says what a product costs under the promotions on (US-55
/// and US-56). The menu and the confirmation both call it, so what the card
/// shows is what the order charges.
/// </summary>
public static class PromotionPricing
{
    /// <summary>
    /// Among the promotions on at <paramref name="now"/> that cover the
    /// product, the one that discounts most. They never add up: two happy hours
    /// overlapping would otherwise sell below what anybody meant.
    /// </summary>
    public static PricedProduct PriceOf(
        IReadOnlyCollection<Promotion> promotions,
        DateTimeOffset now,
        Guid productId,
        decimal regularPrice)
    {
        DateTime local = VenueClock.Local(now);

        Promotion? best = promotions
            .Where(promotion => promotion.Covers(productId) && promotion.IsOnAt(local))
            .MaxBy(promotion => promotion.DiscountPercent);

        return best is null
            ? new PricedProduct(regularPrice, null, null)
            : new PricedProduct(best.PriceOf(regularPrice), regularPrice, best.Name);
    }
}
