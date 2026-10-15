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

    /// <summary>
    /// Persists the new table, or answers false when the venue's unique index
    /// refused its number: another administrator saved it after the
    /// <see cref="NumberExistsAsync"/> check. Saving is the repository's job,
    /// not the caller's.
    /// </summary>
    Task<bool> TryAddAsync(Table table, CancellationToken cancellationToken);

    /// <summary>Persists what changed on a table obtained from <see cref="GetForUpdateAsync"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
