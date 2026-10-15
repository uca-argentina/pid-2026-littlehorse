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
/// One product in one bar for a night: what was loaded into it, what it sold
/// and what is left, beside what the storeroom still has to give.
/// </summary>
/// <remarks>
/// <see cref="Loaded"/> is what was loaded into this night, minus what was
/// taken back out: the bar starts every night empty. <see cref="Sold"/> counts
/// the units in this night's orders, the canceled ones left out.
/// <see cref="InStoreroom"/> is the product's, not the bar's, so it is the
/// same in every bar: the general stock minus what the bars of the nights not
/// over yet hold, right now.
/// </remarks>
public sealed record NightStockLine(Guid ProductId, string ProductName, int Loaded, int Sold, int InStoreroom)
{
    public int Left => Loaded - Sold;
}
