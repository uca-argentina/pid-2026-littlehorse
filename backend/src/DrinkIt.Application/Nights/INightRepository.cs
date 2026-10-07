using DrinkIt.Domain.Nights;

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
    /// is not a parameter: the global query filter scopes it.
    /// </summary>
    Task<bool> OverlapsAsync(DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken);

    /// <summary>Persists the new night. Saving is the repository's job, not the caller's.</summary>
    Task AddAsync(Night night, CancellationToken cancellationToken);
}
