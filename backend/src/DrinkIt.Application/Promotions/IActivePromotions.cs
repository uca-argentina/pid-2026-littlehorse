using DrinkIt.Domain.Promotions;

namespace DrinkIt.Application.Promotions;

/// <summary>
/// The promotions of the current venue that are switched on (US-55). Read only:
/// whether one is on at this moment is <see cref="PromotionPricing"/>'s call.
/// </summary>
public interface IActivePromotions
{
    Task<IReadOnlyList<Promotion>> ListAsync(CancellationToken cancellationToken);
}
