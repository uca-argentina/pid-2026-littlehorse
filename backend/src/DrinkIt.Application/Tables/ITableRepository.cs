using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tables;

/// <summary>
/// The write side of the table aggregate. A repository exists here because there
/// is an invariant to protect: a number is unique inside one venue, put-away
/// tables included, and the check plus the insert have to agree on the venue.
/// </summary>
public interface ITableRepository
{
    /// <summary>
    /// Whether the venue of the current request already has a table with that
    /// number, active or not. The venue is not a parameter on purpose: the global
    /// query filter scopes this to the tenant the request resolved.
    /// </summary>
    Task<bool> NumberExistsAsync(int number, CancellationToken cancellationToken);

    /// <summary>The table tracked for change, or null when this venue has none with that id.</summary>
    Task<Table?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Persists the new table. Saving is the repository's job, not the caller's.</summary>
    Task AddAsync(Table table, CancellationToken cancellationToken);

    /// <summary>Persists what changed on a table obtained from <see cref="GetForUpdateAsync"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
