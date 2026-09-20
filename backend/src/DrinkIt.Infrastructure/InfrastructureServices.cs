using DrinkIt.Application.Authentication;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Security;
using DrinkIt.Application.Staff;
using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Authentication;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Persistence.Seeding;
using DrinkIt.Infrastructure.Security;
using DrinkIt.Infrastructure.Staff;
using DrinkIt.Infrastructure.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Infrastructure;

/// <summary>
/// The single public door into this assembly. Everything it registers is
/// internal, so the only way to reach an implementation from outside is to ask
/// the container for its Application interface.
/// </summary>
public static class InfrastructureServices
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<DrinkItDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DrinkIt")));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<DevelopmentSeedOptions>(configuration.GetSection(DevelopmentSeedOptions.SectionName));
        services.Configure<ImageStorageOptions>(configuration.GetSection(ImageStorageOptions.SectionName));
        services.AddScoped<DevelopmentSeeder>();

        // Injected rather than calling DateTimeOffset.UtcNow, so token expiry
        // can be asserted exactly in tests.
        services.AddSingleton(TimeProvider.System);

        // Stateless and thread-safe, so one instance is enough.
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        // The blob client is thread-safe and holds its own connection pool.
        services.AddSingleton<IImageStore, AzureBlobImageStore>();

        // These hold a DbContext, which is scoped to the request.
        services.AddScoped<IStaffCredentialsQuery, StaffCredentialsQuery>();
        services.AddScoped<IStaffUserRepository, StaffUserRepository>();
        services.AddScoped<IStaffUserQueries, StaffUserQueries>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductQueries, ProductQueries>();
        services.AddScoped<IVenueLookup, VenueLookup>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductsForOrdering, ProductsForOrdering>();
        services.AddScoped<IOrderCodeSequence, OrderCodeSequence>();
        services.AddScoped<IOrderTrackingQueries, OrderTrackingQueries>();

        // Registered as a collection on purpose: the handler picks the strategy
        // that matches the method asked for, so adding cash or VIP balance is
        // adding a class here and touching nothing else.
        services.AddScoped<IPaymentStrategy, DigitalPaymentStrategy>();

        return services;
    }

    /// <summary>
    /// The only way to reach DevelopmentSeeder from outside this assembly: it
    /// stays internal, and this is the door.
    /// </summary>
    public static Task SeedDevelopmentDataAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<DevelopmentSeeder>().SeedAsync(cancellationToken);
}
