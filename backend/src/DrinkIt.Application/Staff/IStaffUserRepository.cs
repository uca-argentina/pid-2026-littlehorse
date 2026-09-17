using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Staff;

/// <summary>
/// The write side of the staff aggregate. A repository exists here and not for
/// every entity because this is where there is an invariant to protect: a
/// username is unique inside one venue, and the check plus the insert have to
/// agree on the same venue.
/// </summary>
public interface IStaffUserRepository
{
    /// <summary>
    /// Whether the venue of the current request already has that username. The
    /// venue is not a parameter on purpose: the global query filter scopes this
    /// to the tenant the request resolved, so no caller can aim it elsewhere.
    /// The username arrives already normalised through
    /// <see cref="StaffUser.NormalizeUsername"/>.
    /// </summary>
    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken);

    /// <summary>Persists the new user. Saving is the repository's job, not the caller's.</summary>
    Task AddAsync(StaffUser user, CancellationToken cancellationToken);

    /// <summary>
    /// The whole aggregate, tracked, so a use case can change it. Returns null
    /// when this venue has nobody with that id — including when another venue
    /// does, because the global query filter hides it either way.
    /// </summary>
    Task<StaffUser?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Commits what the use case changed on a tracked aggregate.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// How many administrators this venue could still sign in with. Read before
    /// taking one away, so the venue is never left unable to administer itself.
    /// </summary>
    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken);
}
