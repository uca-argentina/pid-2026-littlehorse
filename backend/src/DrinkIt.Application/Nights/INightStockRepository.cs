using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Nights;

/// <summary>
/// A night as the stock needs to read it: where it starts, and when the one
/// before it ended — null when it is the venue's first.
/// </summary>
public sealed record NightForStock(Guid Id, DateTimeOffset StartsAt, DateTimeOffset? PreviousEndedAt);

/// <summary>An active product of the venue, with the number it had before nights owned the stock.</summary>
public sealed record StockedProduct(Guid Id, string Name, int InitialStock);

/// <summary>
/// The write side of a night's stock (US-37). The venue is never a parameter:
/// the global query filter scopes every read and write to the tenant of the
/// request.
/// </summary>
public interface INightStockRepository
{
    /// <summary>Null when this venue has no such night, including when another venue does.</summary>
    Task<NightForStock?> FindNightAsync(Guid nightId, CancellationToken cancellationToken);

    /// <summary>The rows this night already has, one per product.</summary>
    Task<IReadOnlyList<NightStock>> ListAsync(Guid nightId, CancellationToken cancellationToken);

    /// <summary>Every active product of the venue: what a night's stock has to cover.</summary>
    Task<IReadOnlyList<StockedProduct>> ListActiveProductsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What each product had left when the latest earlier night of the venue
    /// that has a row for it ended. A product with no earlier row is simply
    /// not in the answer.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CarriedOverAsync(Guid nightId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists rows that did not exist. A row somebody else opened in the
    /// meantime is not an error: the one that got there first stands.
    /// </summary>
    Task AddRangeAsync(IReadOnlyCollection<NightStock> rows, CancellationToken cancellationToken);

    /// <summary>
    /// The row of one product in one night, tracked so the adjustment can be
    /// saved. Null when the night has none, including another venue's night.
    /// </summary>
    Task<NightStock?> GetForUpdateAsync(Guid nightId, Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds <paramref name="change"/> to what the database holds right now,
    /// not to a total worked out in memory: a sale made since the row was read
    /// keeps its units. False, and nothing written, when those sales left less
    /// than the change takes away. Either way <paramref name="stock"/> ends up
    /// holding what the database has.
    /// </summary>
    Task<bool> SaveAdjustmentAsync(NightStock stock, int change, CancellationToken cancellationToken);
}
