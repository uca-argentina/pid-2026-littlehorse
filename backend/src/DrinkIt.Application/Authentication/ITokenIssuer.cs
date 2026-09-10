using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Authentication;

/// <summary>An issued access token and the moment it stops being valid.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface ITokenIssuer
{
    /// <summary>
    /// The venue travels inside the token. From here on the tenant comes from
    /// this claim and never from the URL, so a bartender cannot reach another
    /// venue by editing the address bar.
    /// </summary>
    AccessToken Issue(Guid staffUserId, Guid venueId, string username, StaffRole role);
}
