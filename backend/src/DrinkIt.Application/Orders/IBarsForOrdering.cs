namespace DrinkIt.Application.Orders;

/// <summary>
/// One bar of the night as confirming an order needs it (US-37): how much work
/// it has and how many of each drink it holds. A bar is a KDS.
/// </summary>
/// <param name="KdsId">The KDS account of the bar.</param>
/// <param name="Load">Orders it has waiting or being prepared, the measure of "less work".</param>
/// <param name="Units">What it holds of each drink asked about. A drink it never had is missing.</param>
/// <param name="LastBeatAt">When its tablet last said it was there (US-38). Null if it never did.</param>
public sealed record BarSupply(Guid KdsId, int Load, IReadOnlyDictionary<Guid, int> Units, DateTimeOffset? LastBeatAt = null)
{
    /// <summary>
    /// How long a bar counts as connected after its last beat (US-38). The
    /// tablet beats every twenty seconds, so a short drop of the network does
    /// not make it miss its orders.
    /// </summary>
    public static readonly TimeSpan BeatWindow = TimeSpan.FromMinutes(1);

    public int UnitsOf(Guid productId) => Units.GetValueOrDefault(productId);

    public bool IsConnectedAt(DateTimeOffset now) => LastBeatAt is DateTimeOffset beat && now - beat <= BeatWindow;
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
