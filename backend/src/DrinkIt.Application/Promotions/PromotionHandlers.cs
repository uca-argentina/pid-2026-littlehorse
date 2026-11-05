using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Promotions;

namespace DrinkIt.Application.Promotions;

/// <summary>What the administration sends to define a promotion (US-54).</summary>
public sealed record PromotionDefinition(
    string Name,
    int DiscountPercent,
    IReadOnlyCollection<DayOfWeek> Days,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    IReadOnlyCollection<Guid> ProductIds);

public sealed record UpdatePromotionCommand(Guid Id, PromotionDefinition Definition);

/// <summary>The promotion as the administration screen shows it after a change.</summary>
public sealed record PromotionSummary(
    Guid Id,
    string Name,
    int DiscountPercent,
    IReadOnlyList<DayOfWeek> Days,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    IReadOnlyList<Guid> ProductIds,
    bool IsActive,
    AuditInfo Audit)
{
    public static PromotionSummary Of(Promotion promotion) => new(
        promotion.Id,
        promotion.Name,
        promotion.DiscountPercent,
        promotion.Days,
        promotion.StartsAt,
        promotion.EndsAt,
        promotion.ProductIds,
        promotion.IsActive,
        new AuditInfo(promotion.CreatedAt, promotion.CreatedBy, promotion.LastModifiedAt, promotion.LastModifiedBy));
}

/// <summary>
/// The products of a promotion have to be the venue's own, which takes looking
/// them up: asked here and not in the domain. The venue filter does the work:
/// another venue's product is not found, which is the answer for both.
/// </summary>
internal static class PromotionProducts
{
    public static async Task<Error?> RefusalAsync(
        IReadOnlyCollection<Guid> ids,
        IProductsForOrdering products,
        CancellationToken cancellationToken)
    {
        Guid[] distinct = [.. ids.Distinct()];

        // Duplicates and an empty list are the domain's to refuse, with its own code.
        if (distinct.Length != ids.Count || distinct.Length == 0) return null;

        return (await products.GetForOrderingAsync(distinct, cancellationToken)).Count == distinct.Length
            ? null
            : PromotionErrors.ProductNotFound;
    }
}

/// <summary>US-54, criterion 1: adds a promotion to the signed-in administrator's venue, active.</summary>
public sealed class CreatePromotionHandler(
    IPromotionRepository promotions,
    IProductsForOrdering products,
    ICurrentVenue currentVenue)
{
    public async Task<Result<PromotionSummary>> HandleAsync(PromotionDefinition command, CancellationToken cancellationToken)
    {
        if (await PromotionProducts.RefusalAsync(command.ProductIds, products, cancellationToken) is Error refusal) return refusal;

        // What is left wrong is a broken domain invariant: the API answers 400.
        Promotion promotion = Promotion.Create(
            currentVenue.Id,
            command.Name,
            command.DiscountPercent,
            command.Days,
            command.StartsAt,
            command.EndsAt,
            command.ProductIds);

        await promotions.AddAsync(promotion, cancellationToken);

        return PromotionSummary.Of(promotion);
    }
}

/// <summary>Replaces what a promotion is for the orders to come. The confirmed ones keep their price.</summary>
public sealed class UpdatePromotionHandler(IPromotionRepository promotions, IProductsForOrdering products)
{
    public async Task<Result<PromotionSummary>> HandleAsync(UpdatePromotionCommand command, CancellationToken cancellationToken)
    {
        Promotion? promotion = await promotions.GetForUpdateAsync(command.Id, cancellationToken);

        if (promotion is null) return PromotionErrors.NotFound;

        PromotionDefinition definition = command.Definition;

        if (await PromotionProducts.RefusalAsync(definition.ProductIds, products, cancellationToken) is Error refusal) return refusal;

        promotion.Update(
            definition.Name,
            definition.DiscountPercent,
            definition.Days,
            definition.StartsAt,
            definition.EndsAt,
            definition.ProductIds);

        await promotions.SaveChangesAsync(cancellationToken);

        return PromotionSummary.Of(promotion);
    }
}

/// <summary>US-54, criterion 4: switches a promotion on or off. Off stops it for new orders only.</summary>
public sealed class SetPromotionActiveHandler(IPromotionRepository promotions)
{
    public async Task<Result<PromotionSummary>> HandleAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        Promotion? promotion = await promotions.GetForUpdateAsync(id, cancellationToken);

        if (promotion is null) return PromotionErrors.NotFound;

        if (active) promotion.Activate();
        else promotion.Deactivate();

        await promotions.SaveChangesAsync(cancellationToken);

        return PromotionSummary.Of(promotion);
    }
}
