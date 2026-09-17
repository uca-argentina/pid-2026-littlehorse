namespace DrinkIt.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>At least 32 bytes: HMAC-SHA256 refuses anything shorter.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "drinkit";

    public string Audience { get; set; } = "drinkit";

    /// <summary>
    /// Short on purpose. A stolen token stays useful for this long, and staff
    /// work a whole night on the same device, so this is the balance between
    /// re-login friction and exposure.
    /// </summary>
    public int LifetimeMinutes { get; set; } = 480;
}
