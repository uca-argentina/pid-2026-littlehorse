using DrinkIt.Application.Waiter;

namespace DrinkIt.Api.Features.Waiter;

/// <summary>What the waiter's endpoints need. Scoped: one per request, same as the DbContext below them.</summary>
internal static class WaiterHandlers
{
    public static IServiceCollection AddWaiterHandlers(this IServiceCollection services) => services
        .AddScoped<TakeTableOrderHandler>();
}
