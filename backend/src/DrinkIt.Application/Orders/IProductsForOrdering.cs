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
///
/// It has no save of its own, deliberately: what is sold from these products is
/// written by IOrderRepository.AddAsync, in the same unit of work as the order
/// that sold it. A port promising to save here would be a port that can break
/// that, and the review of 2026-09-17 was right that one that says it saves and
/// does not is worse than none at all.
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
}
