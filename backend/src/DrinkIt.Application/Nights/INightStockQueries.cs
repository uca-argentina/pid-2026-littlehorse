namespace DrinkIt.Application.Nights;

/// <summary>
/// The read side of a night's stock (US-37), bar by bar. No repository: it is
/// a sum over the night's adjustments and orders, projected straight from the
/// DbContext.
/// </summary>
public interface INightStockQueries
{
    /// <summary>
    /// The night's stock: one section per KDS of its crew, by name, and one
    /// line per product in each. Null when this venue has no such night.
    /// </summary>
    Task<NightStockSheet?> GetAsync(Guid nightId, CancellationToken cancellationToken);
}

public sealed record NightStockSheet(Guid NightId, IReadOnlyList<NightStockBar> Bars);

/// <summary>One bar of the night: a KDS of its crew, by the name it signs in with.</summary>
public sealed record NightStockBar(Guid KdsId, string Name, IReadOnlyList<NightStockLine> Lines);

/// <summary>
/// One product in one bar for a night: what it started with, what was loaded into it, what
/// it sold and what is left.
/// </summary>
/// <remarks>
/// <see cref="Carried"/> is what the night before left, which is what this one
/// starts with. <see cref="Added"/> is what was loaded into this night, minus
/// what was taken out of it. <see cref="Sold"/> counts the units in this
/// night's orders, the canceled ones left out.
/// </remarks>
public sealed record NightStockLine(Guid ProductId, string ProductName, int Carried, int Added, int Sold)
{
    /// <summary>Criterion 3's "cuánto se cargó": what it started with and what came in on top.</summary>
    public int Loaded => Carried + Added;

    public int Left => Loaded - Sold;

    /// <summary>
    /// The line worked back from the shelf. What the bar holds is what is left
    /// right now, after this night and every one after it, so taking away what
    /// they moved leaves what this one started with.
    /// </summary>
    /// <remarks>
    /// <c>shelf</c> is what the bar holds of it right now; <c>netOfLaterNights</c>,
    /// what every later night loaded minus what it sold. Nothing is stored as
    /// "carried": a cancellation that lands days later moves the shelf and its
    /// own night, and every number here follows on its own.
    /// </remarks>
    public static NightStockLine From(Guid productId, string productName, int shelf, int added, int sold, int netOfLaterNights) =>
        new(productId, productName, shelf - netOfLaterNights - (added - sold), added, sold);
}
