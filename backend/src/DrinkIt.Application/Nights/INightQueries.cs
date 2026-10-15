namespace DrinkIt.Application.Nights;

/// <summary>
/// The read side. No repository: reads have no invariant to protect, so the
/// implementation projects straight from the DbContext.
/// </summary>
public interface INightQueries
{
    /// <summary>Every night of the venue of the current request, the latest first.</summary>
    Task<IReadOnlyList<NightSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One night of the current venue, or null — also when it is another venue's.</summary>
    Task<NightSummary?> GetAsync(Guid id, CancellationToken cancellationToken);
}
