namespace DrinkIt.Application.Menu;

/// <summary>What the administration listing shows about one product.</summary>
public sealed record ProductListItem(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    int Stock,
    bool IsAvailable,
    bool IsSoldOut,
    bool IsActive);

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
}
