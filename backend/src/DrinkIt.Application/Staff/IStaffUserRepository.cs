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
}
