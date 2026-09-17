using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Orders;

/// <summary>
/// The menu as an order needs it: the real products of this venue, tracked, so
/// what they cost is read from them and what was sold is taken off them.
/// </summary>
/// <remarks>
/// Separate from IProductRepository, which serves the administration screens.
/// This one exists so that confirming an order cannot reach the operations that
/// edit the menu: the only thing it can do to a product is sell it.
/// </remarks>
public interface IProductsForOrdering
{
    /// <summary>
    /// Whichever of those products this venue has, tracked for update. Ids it
    /// does not sell simply do not come back — telling them apart is the
    /// handler's business, and another venue's product is not one of ours.
    /// </summary>
    Task<IReadOnlyList<Product>> GetForOrderingAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
