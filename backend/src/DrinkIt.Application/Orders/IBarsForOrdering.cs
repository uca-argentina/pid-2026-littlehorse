namespace DrinkIt.Application.Orders;

/// <summary>
/// One bar of the night as confirming an order needs it (US-37): how much work
/// it has and how many of each drink it holds. A bar is a KDS.
/// </summary>
/// <param name="KdsId">The KDS account of the bar.</param>
/// <param name="Load">Orders it has waiting or being prepared, the measure of "less work".</param>
/// <param name="Units">What it holds of each drink asked about. A drink it never had is missing.</param>
public sealed record BarSupply(Guid KdsId, int Load, IReadOnlyDictionary<Guid, int> Units)
{
    public int UnitsOf(Guid productId) => Units.GetValueOrDefault(productId);
}

/// <summary>A drink and how many of it, as the bars are asked for them.</summary>
public sealed record OrderLine(Guid ProductId, int Quantity);

/// <summary>
/// The bars an order can go to. Read-only, like IProductsForOrdering: the
/// stock is taken by IOrderRepository.AddAsync with a conditional statement,
/// in the same unit of work as the order.
/// </summary>
public interface IBarsForOrdering
{
    /// <summary>
    /// The active KDS of that night's crew, each with its load and its units of
    /// those drinks, in a stable order. Empty when the night has no bar.
    /// </summary>
    Task<IReadOnlyList<BarSupply>> GetAsync(Guid nightId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}
