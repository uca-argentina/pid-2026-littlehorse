using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Nights;

/// <summary>
/// The write side of the night aggregate. The invariant it protects spans
/// rows: two nights of a venue never overlap, so the check and the insert have
/// to agree on the same venue.
/// </summary>
public interface INightRepository
{
    /// <summary>
    /// Whether a night of the current venue shares any moment with those hours.
    /// Touching is not overlapping: one ending at 06:00 and the next starting at
    /// 06:00 is fine, since <see cref="Night.EndsAt"/> is exclusive. The venue
    /// is not a parameter: the global query filter scopes it. <c>excluding</c>
    /// is the night being edited, which never overlaps itself.
    /// </summary>
    Task<bool> OverlapsAsync(DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excluding, CancellationToken cancellationToken);

    /// <summary>
    /// The whole aggregate, tracked, so a use case can change it. Null when this
    /// venue has no such night, including when another venue does.
    /// </summary>
    Task<Night?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Commits what the use case changed on a tracked aggregate.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the new night with what its KDS start with (US-37): each
    /// active product's storeroom at <paramref name="now"/>, split by
    /// <see cref="Night.LoadOpeningStock"/> among <paramref name="bars"/>, every
    /// load on record. The night and its stock, or neither. Saving is the
    /// repository's job, not the caller's.
    /// </summary>
    Task AddAsync(Night night, IReadOnlyList<StaffUser> bars, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The KDS that hold at least one unit of something in that night (US-37).</summary>
    Task<IReadOnlyCollection<Guid>> BarsHoldingStockAsync(Guid nightId, CancellationToken cancellationToken);
}
