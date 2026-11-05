using DrinkIt.Application.StampCards;

namespace DrinkIt.Api.Features.StampCards;

internal static class StampCardHandlers
{
    public static IServiceCollection AddStampCardHandlers(this IServiceCollection services) => services
        .AddScoped<SaveStampCardRuleHandler>()
        .AddScoped<GetStampCardHandler>();
}
