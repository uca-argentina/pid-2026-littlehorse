using DrinkIt.Application.Promotions;
using DrinkIt.Domain.Promotions;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Promotions;

internal sealed class PromotionRepository(DrinkItDbContext context) : IPromotionRepository
{
    public async Task AddAsync(Promotion promotion, CancellationToken cancellationToken)
    {
        context.Promotions.Add(promotion);

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<Promotion?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        context.Promotions.SingleOrDefaultAsync(promotion => promotion.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// The switched-on promotions, as aggregates: whether one is on at this moment
/// is a rule of the domain, so it is asked there and not restated in SQL. A
/// venue has a handful, so reading them all is cheap.
/// </summary>
internal sealed class ActivePromotions(DrinkItDbContext context) : IActivePromotions
{
    public async Task<IReadOnlyList<Promotion>> ListAsync(CancellationToken cancellationToken) =>
        await context.Promotions
            .AsNoTracking()
            .Where(promotion => promotion.IsActive)
            .ToListAsync(cancellationToken);
}

internal sealed class PromotionQueries(DrinkItDbContext context) : IPromotionQueries
{
    public async Task<IReadOnlyList<PromotionListItem>> ListAsync(CancellationToken cancellationToken)
    {
        // The days and the products are read through the aggregate: the days
        // are a bit set that SQL has no reason to unpack. A venue has a handful.
        List<Promotion> promotions = await context.Promotions
            .AsNoTracking()
            .OrderByDescending(promotion => promotion.IsActive)
            .ThenBy(promotion => promotion.Name)
            .ToListAsync(cancellationToken);

        return [.. promotions.Select(promotion =>
        {
            PromotionSummary summary = PromotionSummary.Of(promotion);

            return new PromotionListItem(
                summary.Id,
                summary.Name,
                summary.DiscountPercent,
                summary.Days,
                summary.StartsAt,
                summary.EndsAt,
                summary.ProductIds,
                summary.IsActive,
                summary.Audit);
        })];
    }
}
