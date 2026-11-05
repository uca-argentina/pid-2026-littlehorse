using DrinkIt.Application.Common;

namespace DrinkIt.Application.Promotions;

/// <summary>What the administration listing shows about one promotion.</summary>
public sealed record PromotionListItem(
    Guid Id,
    string Name,
    int DiscountPercent,
    IReadOnlyList<DayOfWeek> Days,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    IReadOnlyList<Guid> ProductIds,
    bool IsActive,
    AuditInfo Audit);

/// <summary>The read side: no repository, a projection straight to the DTO.</summary>
public interface IPromotionQueries
{
    /// <summary>Every promotion of the venue, deactivated ones included, active first.</summary>
    Task<IReadOnlyList<PromotionListItem>> ListAsync(CancellationToken cancellationToken);
}
