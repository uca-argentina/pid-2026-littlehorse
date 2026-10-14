using DrinkIt.Application.Tables;

namespace DrinkIt.Api.Features.Tables;

/// <summary>What the table endpoints need. Scoped: one per request, same as the DbContext below them.</summary>
internal static class TableHandlers
{
    public static IServiceCollection AddTableHandlers(this IServiceCollection services) => services
        .AddScoped<CreateTableHandler>()
        .AddScoped<DeactivateTableHandler>()
        .AddScoped<ReactivateTableHandler>();
}
