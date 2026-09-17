namespace DrinkIt.Infrastructure.Persistence.Seeding;

public sealed class DevelopmentSeedOptions
{
    public const string SectionName = "DevelopmentSeed";

    public string VenueName { get; set; } = "Bar Alfa";

    public string VenueSlug { get; set; } = "bar-alfa";

    public string AdminUsername { get; set; } = "admin";

    /// <summary>
    /// Set in launchSettings.json, not here: no default lives in code, so that
    /// running against something other than the local Docker database (see
    /// docker-compose.yml, same reasoning) requires deciding on a password
    /// instead of inheriting one silently. Blank fails loudly rather than
    /// seeding a guessable account.
    /// </summary>
    public string AdminPassword { get; set; } = string.Empty;
}
