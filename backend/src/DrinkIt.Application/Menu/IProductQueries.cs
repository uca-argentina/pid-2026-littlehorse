using DrinkIt.Application.Common;

namespace DrinkIt.Application.Menu;

/// <summary>What the administration listing shows about one product.</summary>
public sealed record ProductListItem(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    int Stock,
    Guid CategoryId,
    bool IsAvailable,
    bool IsSoldOut,
    bool IsActive,
    AuditInfo Audit);

/// <summary>
/// One card of the menu a customer reads on their phone.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="ProductListItem"/>. This one answers
/// to somebody with no session, so it carries what the card draws and nothing
/// else: how many are left is the venue's business, and whether a product
/// cannot be served because it ran out or because the venue switched it off is
/// a distinction the customer has no use for.
/// </remarks>
public sealed record MenuItem(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    Guid CategoryId,
    bool IsOrderable);

/// <summary>
/// The read side. No repository here: reads have no invariant to protect, so
/// the implementation projects straight from the DbContext to this DTO.
/// </summary>
public interface IProductQueries
{
    /// <summary>
    /// Every product of the venue of the current request, deactivated ones
    /// included: a soft-deleted product still has to be visible to be brought
    /// back.
    /// </summary>
    Task<IReadOnlyList<ProductListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What the venue is selling right now, for the customer's menu. Leaves out
    /// whatever was taken off the menu, and keeps whatever cannot be served
    /// tonight: a product that vanished reads as a mistake and sends somebody
    /// to ask at the bar, which is the walk this product exists to avoid.
    /// </summary>
    Task<IReadOnlyList<MenuItem>> ListForMenuAsync(CancellationToken cancellationToken);
}
