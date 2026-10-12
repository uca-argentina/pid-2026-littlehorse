using DrinkIt.Application.Menu;

namespace DrinkIt.Api.Features.Menu;

/// <summary>What the menu, category and product endpoints need. Scoped: one per request, same as the DbContext below them.</summary>
internal static class MenuHandlers
{
    public static IServiceCollection AddMenuHandlers(this IServiceCollection services) => services
        .AddScoped<CreateCategoryHandler>()
        .AddScoped<CreateProductHandler>()
        .AddScoped<UpdateProductHandler>()
        .AddScoped<DeactivateProductHandler>()
        .AddScoped<UploadProductImageHandler>()
        .AddScoped<MarkProductUnavailableHandler>()
        .AddScoped<MarkProductAvailableHandler>();
}
