using DrinkIt.Application.Authentication;

namespace DrinkIt.Api.Features.Authentication;

/// <summary>What the login endpoint needs. Scoped: one per request, same as the DbContext below it.</summary>
internal static class LoginHandlers
{
    public static IServiceCollection AddLoginHandlers(this IServiceCollection services) =>
        services.AddScoped<LoginHandler>();
}
