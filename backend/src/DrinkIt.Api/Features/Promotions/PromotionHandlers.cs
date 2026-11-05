using DrinkIt.Application.Promotions;

namespace DrinkIt.Api.Features.Promotions;

internal static class PromotionHandlers
{
    public static IServiceCollection AddPromotionHandlers(this IServiceCollection services) => services
        .AddScoped<CreatePromotionHandler>()
        .AddScoped<UpdatePromotionHandler>()
        .AddScoped<SetPromotionActiveHandler>();
}
