using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Staff;

/// <summary>
/// Someone who works at a venue, in one of the roles of <see cref="StaffRole"/>.
/// Customers are a separate aggregate — they belong to no venue and their
/// account is optional.
/// </summary>
public sealed class StaffUser : AuditStamps, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "staff.venue_required";
        public const string UsernameRequired = "staff.username_required";
        public const string UsernameLength = "staff.username_length";
        public const string UsernameWhitespace = "staff.username_whitespace";
        public const string PasswordHashRequired = "staff.password_hash_required";
        public const string RoleInvalid = "staff.role_invalid";
    }

    private const int UsernameMinLength = 3;
    private const int UsernameMaxLength = 50;

    private StaffUser(Guid id, Guid venueId, string username, string passwordHash, StaffRole role)
    {
        Id = id;
        VenueId = venueId;
        Username = username;
        PasswordHash = passwordHash;
        Role = role;
        IsActive = true;
    }

    public Guid Id { get; }

    /// <summary>Never empty: the tenant claim in the token is built from this.</summary>
    public Guid VenueId { get; }

    public string Username { get; private set; }

    public string PasswordHash { get; private set; }

    public StaffRole Role { get; private set; }

    /// <summary>False after the ABM's soft delete. Rows are never removed.</summary>
    public bool IsActive { get; private set; }

    public static StaffUser Create(Guid venueId, string username, string passwordHash, StaffRole role)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Staff users must belong to a venue.");
        if (string.IsNullOrWhiteSpace(username)) throw new DomainException(ErrorCodes.UsernameRequired, "Username is required.");

        string cleanUsername = NormalizeUsername(username);

        if (cleanUsername.Length is < UsernameMinLength or > UsernameMaxLength) throw new DomainException(ErrorCodes.UsernameLength, $"Username must be {UsernameMinLength}-{UsernameMaxLength} characters.");
        if (cleanUsername.Any(char.IsWhiteSpace)) throw new DomainException(ErrorCodes.UsernameWhitespace, "Username cannot contain whitespace.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException(ErrorCodes.PasswordHashRequired, "Password hash is required.");
        if (!Enum.IsDefined(role)) throw new DomainException(ErrorCodes.RoleInvalid, $"'{role}' is not a valid staff role.");

        return new StaffUser(Guid.CreateVersion7(), venueId, cleanUsername, passwordHash, role);
    }

    /// <summary>
    /// The one place that decides what a username looks like once stored. Login
    /// normalises the typed username through here too, so matching never
    /// depends on the database collation.
    /// </summary>
    /// <remarks>
    /// Invariant culture on purpose: ToLower() in a Turkish locale maps 'I' to a
    /// dotless 'ı', so the same input would yield a different username depending
    /// on the machine's regional settings.
    /// </remarks>
    public static string NormalizeUsername(string username) =>
        (username ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Fixes a role that was assigned wrong. The person keeps their account, so
    /// whatever they did under the old role still points at them.
    /// </summary>
    public void ChangeRole(StaffRole role)
    {
        if (!Enum.IsDefined(role)) throw new DomainException(ErrorCodes.RoleInvalid, $"'{role}' is not a valid staff role.");

        Role = role;
    }

    /// <summary>
    /// Replaces a forgotten password. Nothing can recover the old one — only its
    /// hash was ever stored — so the administrator sets a new one and hands it
    /// over again.
    /// </summary>
    public void ChangePassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException(ErrorCodes.PasswordHashRequired, "Password hash is required.");

        PasswordHash = passwordHash;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
