using DrinkIt.Application.Promotions;
using DrinkIt.Domain.Promotions;

namespace DrinkIt.Application.Menu;

/// <summary>
/// The menu a customer reads, with the promotions on right now priced in
/// (US-56). Prices come from <see cref="PromotionPricing"/>, the same rule the
/// confirmation uses.
/// </summary>
public sealed class ListMenuHandler(IProductQueries products, IActivePromotions promotions, TimeProvider clock)
{
    public async Task<IReadOnlyList<MenuItem>> HandleAsync(Guid? nightId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MenuItem> menu = await products.ListForMenuAsync(nightId, cancellationToken);
        IReadOnlyList<Promotion> active = await promotions.ListAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();

        return [.. menu.Select(item =>
        {
            PricedProduct priced = PromotionPricing.PriceOf(active, now, item.Id, item.Price);

            return item with { Price = priced.Price, RegularPrice = priced.RegularPrice, PromotionName = priced.PromotionName };
        })];
    }
}
