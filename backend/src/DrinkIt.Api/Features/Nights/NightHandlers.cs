using DrinkIt.Application.Nights;

namespace DrinkIt.Api.Features.Nights;

/// <summary>What the night administration endpoints need. Scoped, like the DbContext below them.</summary>
internal static class NightHandlers
{
    public static IServiceCollection AddNightHandlers(this IServiceCollection services) => services
        .AddScoped<CreateNightHandler>();
}
