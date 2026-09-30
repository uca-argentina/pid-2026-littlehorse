using DrinkIt.Application.Cashier;

namespace DrinkIt.Api.Features.Cashier;

/// <summary>What the till's endpoints need. Scoped: one per request, same as the DbContext below them.</summary>
internal static class CashierHandlers
{
    public static IServiceCollection AddCashierHandlers(this IServiceCollection services) => services
        .AddScoped<CollectCashHandler>()
        .AddScoped<MyCollectionsHandler>();
}
