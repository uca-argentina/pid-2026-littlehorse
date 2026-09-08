namespace DrinkIt.Infrastructure.Authentication;

/// <summary>
/// The claim names the token is issued with. Public and shared on purpose:
/// the issuer writes them, the bearer validation reads them, and the venue
/// middleware reads them again. Three copies of the same string is three
/// chances for authentication to break silently when one of them changes.
/// </summary>
/// <remarks>
/// Short names instead of the WS-Federation URIs ASP.NET Core defaults to.
/// Those are around sixty characters each and travel on every request; on a PWA
/// over mobile data that is not free.
/// </remarks>
public static class JwtClaims
{
    public const string Venue = "venue_id";

    public const string Role = "role";

    public const string Name = "name";
}
