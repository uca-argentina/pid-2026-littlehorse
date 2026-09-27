namespace DrinkIt.Api.Common;

/// <summary>
/// Deployed, the PWA and the API are on different hosts, so the browser needs
/// the API's permission for every call. The origins come from configuration
/// (Cors:AllowedOrigins), set by the deploy from the Static Web App's address
/// and the custom domain: nothing here knows either of them.
/// </summary>
internal static class FrontendCors
{
    public const string SectionName = "Cors:AllowedOrigins";

    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        // Empty in Development: the Angular proxy makes every call same-origin.
        string[] origins = configuration.GetSection(SectionName).Get<string[]>() ?? [];

        // Any header and method, but only these origins. The token travels in
        // the Authorization header and not in a cookie, so no credentials mode.
        return services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()));
    }
}
