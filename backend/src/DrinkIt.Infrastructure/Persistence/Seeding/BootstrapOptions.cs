namespace DrinkIt.Infrastructure.Persistence.Seeding;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string VenueName { get; set; } = "Bar Alfa";

    public string VenueSlug { get; set; } = "bar-alfa";

    public string AdminUsername { get; set; } = "admin";

    /// <summary>
    /// Set in launchSettings.json for Development and as a Container App secret
    /// in production, never here: no default lives in code, so that a database
    /// other than the local Docker one gets a password somebody decided on
    /// instead of inheriting one silently. Blank seeds nothing rather than a
    /// guessable account.
    /// </summary>
    public string AdminPassword { get; set; } = string.Empty;
}
