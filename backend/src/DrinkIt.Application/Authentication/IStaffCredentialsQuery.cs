namespace DrinkIt.Application.Authentication;

public interface IStaffCredentialsQuery
{
    /// <summary>
    /// Looks a staff user up by username alone. The venue is not a parameter on
    /// purpose: it was already resolved from the slug in the URL before this
    /// runs, and the global query filter scopes the lookup to it. Usernames are
    /// only unique within a venue, so this is a single row.
    /// </summary>
    Task<StaffCredentials?> FindAsync(string username, CancellationToken cancellationToken);
}
