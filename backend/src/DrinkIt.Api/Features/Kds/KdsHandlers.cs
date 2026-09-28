using DrinkIt.Application.Kds;

namespace DrinkIt.Api.Features.Kds;

/// <summary>What the bar's endpoints need. Scoped: one per request, same as the DbContext below them.</summary>
internal static class KdsHandlers
{
    public static IServiceCollection AddKdsHandlers(this IServiceCollection services) => services
        .AddScoped<StartPreparingHandler>()
        .AddScoped<ReturnToQueueHandler>();
}
