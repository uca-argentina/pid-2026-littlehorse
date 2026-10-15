using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Metrics;

/// <summary>
/// The read side of a night's report (US-47 to US-49): its paid orders and
/// what each product sold. No repository, like every query: projected straight
/// from the DbContext. What it all adds up to is <see cref="NightMetrics"/>'s
/// call, so it can be tested without a database.
/// </summary>
public interface INightMetricsQueries
{
    /// <summary>Null when this venue has no such night.</summary>
    Task<NightSales?> GetAsync(Guid nightId, CancellationToken cancellationToken);
}

/// <summary>
/// Everything paid in one night: its bars, the orders, and the units of each
/// product in them.
/// </summary>
public sealed record NightSales(IReadOnlyList<NightBar> Bars, IReadOnlyList<PaidOrder> Orders, IReadOnlyList<ProductSales> Products);

/// <summary>
/// A KDS of the night's crew, by the name its board and the customer read
/// (US-36): its display name, or the username when it has none.
/// </summary>
public sealed record NightBar(Guid KdsId, string Name);

/// <summary>
/// One paid order of the night with its stamps. <see cref="ReadyAt"/> and
/// <see cref="DeliveredAt"/> are null until it gets there, and also on orders
/// from before they were kept.
/// </summary>
public sealed record PaidOrder(
    Guid? KdsId,
    PaymentMethod Method,
    decimal Total,
    OrderStatus Status,
    DateTimeOffset PaidAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? DeliveredAt);

/// <summary>
/// One product across the night's paid orders. Name and price come from the
/// order lines, frozen when it was ordered, so a product renamed or removed
/// since still shows what it billed that night.
/// </summary>
public sealed record ProductSales(Guid ProductId, string ProductName, int Units, decimal Billed);
