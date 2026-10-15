using DrinkIt.Application.Nights;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Metrics;

/// <summary>What was paid with one method (US-48).</summary>
public sealed record SalesByMethod(PaymentMethod Method, int Orders, decimal Amount);

/// <summary>
/// How long a stage took (US-47): on average, and on average for the slowest
/// tenth of the orders. Both null when no order has been through it yet.
/// </summary>
public sealed record StageTimes(TimeSpan? Average, TimeSpan? WorstTenth);

/// <summary>From paid to ready at one bar of the night (US-47, criterion 2).</summary>
public sealed record BarTimes(Guid KdsId, string Name, StageTimes PaidToReady);

/// <summary>One of the most ordered products (US-49), against what its bars were loaded with.</summary>
public sealed record TopProduct(Guid ProductId, string Name, int Units, decimal Billed, int Loaded, bool SoldOut);

/// <summary>
/// A night's report: what was sold and how (US-48), how long it took (US-47)
/// and what was ordered most (US-49).
/// </summary>
public sealed record NightMetrics(
    decimal Sold,
    int Orders,
    decimal AverageTicket,
    int Units,
    IReadOnlyList<SalesByMethod> ByMethod,
    StageTimes PaidToReady,
    StageTimes ReadyToDelivered,
    IReadOnlyList<BarTimes> ByBar,
    int Unmeasured,
    IReadOnlyList<TopProduct> Top,
    int SoldOutProducts)
{
    /// <summary>US-49: the ranking shows this many.</summary>
    public const int TopSize = 10;

    /// <summary>The same rows every night, in this order, even at zero.</summary>
    private static readonly PaymentMethod[] Methods = [PaymentMethod.Cash, PaymentMethod.Digital, PaymentMethod.VipBalance];

    public static NightMetrics Build(NightSales sales, NightStockSheet stock)
    {
        IReadOnlyList<PaidOrder> orders = sales.Orders;
        decimal sold = orders.Sum(order => order.Total);

        // The night's stock, product by product across its bars: what came in
        // and what is left of it.
        Dictionary<Guid, (int Loaded, int Left)> loaded = stock.Bars
            .SelectMany(bar => bar.Lines)
            .GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => (group.Sum(line => line.Loaded), group.Sum(line => line.Left)));

        return new NightMetrics(
            sold,
            orders.Count,
            orders.Count == 0 ? 0m : sold / orders.Count,
            sales.Products.Sum(product => product.Units),
            [.. Methods.Select(method => SalesOf(orders, method))],
            Times(orders.Select(PaidToReadyOf)),
            Times(orders.Select(ReadyToDeliveredOf)),
            [.. sales.Bars.Select(bar => new BarTimes(bar.KdsId, bar.Name, Times(orders.Where(order => order.KdsId == bar.KdsId).Select(PaidToReadyOf))))],
            orders.Count(IsUnmeasured),
            [.. sales.Products
                .OrderByDescending(product => product.Units)
                .ThenByDescending(product => product.Billed)
                .Take(TopSize)
                .Select(product => TopOf(product, loaded))],
            loaded.Values.Count(RanOut));
    }

    /// <summary>
    /// What entered by one method, and how many orders it entered for. A
    /// table's order that paid a difference (US-42) is in two rows, each with
    /// its own part, so the rows add up to what was sold but their orders may
    /// add up to more.
    /// </summary>
    private static SalesByMethod SalesOf(IReadOnlyList<PaidOrder> orders, PaymentMethod method)
    {
        decimal[] parts = [.. orders.Select(order => PartOf(order, method)).Where(part => part > 0)];

        return new SalesByMethod(method, parts.Length, parts.Sum());
    }

    private static decimal PartOf(PaidOrder order, PaymentMethod method)
    {
        if (order.Method != PaymentMethod.VipBalance) return order.Method == method ? order.Total : 0m;

        decimal fromBalance = order.BalanceAmount ?? order.Total;

        return method switch
        {
            PaymentMethod.VipBalance => fromBalance,
            PaymentMethod.Digital => order.Total - fromBalance,
            _ => 0m,
        };
    }

    private static TimeSpan? PaidToReadyOf(PaidOrder order) => order.ReadyAt - order.PaidAt;

    private static TimeSpan? ReadyToDeliveredOf(PaidOrder order) => order.DeliveredAt - order.ReadyAt;

    // US-47, criterion 3: it got past ready, but from before ReadyAt was kept,
    // so it has no time to give and none is made up for it.
    private static bool IsUnmeasured(PaidOrder order) =>
        order.ReadyAt is null && order.Status is OrderStatus.Ready or OrderStatus.Delivered;

    /// <summary>
    /// The average, and the average of the slowest tenth (at least one order).
    /// Not the 90th percentile: with 200 quick orders and 20 of half an hour,
    /// that lands on a quick one and hides the twenty who complained.
    /// </summary>
    private static StageTimes Times(IEnumerable<TimeSpan?> measured)
    {
        TimeSpan[] times = [.. measured.OfType<TimeSpan>().Order()];

        if (times.Length == 0) return new StageTimes(null, null);

        int slowest = (int)Math.Ceiling(times.Length / 10.0);

        return new StageTimes(Mean(times), Mean(times[^slowest..]));
    }

    private static TimeSpan Mean(TimeSpan[] times) => TimeSpan.FromTicks((long)times.Average(time => time.Ticks));

    private static TopProduct TopOf(ProductSales product, Dictionary<Guid, (int Loaded, int Left)> loaded)
    {
        (int units, int left) = loaded.GetValueOrDefault(product.ProductId);

        return new TopProduct(product.ProductId, product.ProductName, product.Units, product.Billed, units, RanOut((units, left)));
    }

    // Never loaded is not "ran out": there was nothing to run out of.
    private static bool RanOut((int Loaded, int Left) stock) => stock.Loaded > 0 && stock.Left == 0;
}
