namespace DrinkIt.Application.Tables;

/// <summary>The read side. No repository here: reads have no invariant to protect.</summary>
public interface ITableQueries
{
    /// <summary>
    /// Every table of the venue of the current request, put-away ones included
    /// so they can be brought back, ordered by number.
    /// </summary>
    Task<IReadOnlyList<TableSummary>> ListAsync(CancellationToken cancellationToken);
}
