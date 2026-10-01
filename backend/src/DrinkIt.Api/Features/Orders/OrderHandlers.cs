using DrinkIt.Application.Orders;

namespace DrinkIt.Api.Features.Orders;

/// <summary>What the order endpoints need. Scoped: one per request, same as the DbContext below them.</summary>
internal static class OrderHandlers
{
    public static IServiceCollection AddOrderHandlers(this IServiceCollection services) =>
        services
            .AddScoped<ConfirmOrderHandler>()
            .AddScoped<CancelOrderHandler>();
}
