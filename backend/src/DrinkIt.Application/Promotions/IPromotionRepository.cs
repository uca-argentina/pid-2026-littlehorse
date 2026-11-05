using DrinkIt.Domain.Promotions;

namespace DrinkIt.Application.Promotions;

public interface IPromotionRepository
{
    Task AddAsync(Promotion promotion, CancellationToken cancellationToken);

    Task<Promotion?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
