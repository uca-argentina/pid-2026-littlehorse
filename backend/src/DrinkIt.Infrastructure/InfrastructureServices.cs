using DrinkIt.Application.Authentication;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Security;
using DrinkIt.Application.Staff;
using DrinkIt.Application.Venues;
using DrinkIt.Infrastructure.Authentication;
using DrinkIt.Infrastructure.Cashier;
using DrinkIt.Infrastructure.Kds;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Nights;
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
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<DrinkItDbContext>((services, options) => options
            .UseSqlServer(configuration.GetConnectionString("DrinkIt"))
            // US-30: the one place that stamps who and when on every save.
            .AddInterceptors(services.GetRequiredService<AuditInterceptor>()));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        services.Configure<ImageStorageOptions>(configuration.GetSection(ImageStorageOptions.SectionName));
        services.AddScoped<BootstrapSeeder>();

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
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICategoryQueries, CategoryQueries>();
        services.AddScoped<IVenueLookup, VenueLookup>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductsForOrdering, ProductsForOrdering>();
        services.AddScoped<IOrderCodeSequence, OrderCodeSequence>();
        services.AddScoped<IOrderTrackingQueries, OrderTrackingQueries>();
        services.AddScoped<IKdsQueueQueries, KdsQueueQueries>();
        services.AddScoped<ICashierQueries, CashierQueries>();
        services.AddScoped<INightRepository, NightRepository>();
        services.AddScoped<INightStockRepository, NightStockRepository>();
        services.AddScoped<INightQueries, NightQueries>();
        services.AddScoped<IUnderwayNightLookup, UnderwayNightLookup>();
        services.AddScoped<ITonightsCrew, TonightsCrew>();

        // Implemented in Application, not here — same reason as DigitalPaymentStrategy
        // below — but registered from this composition root either way.
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // SignalR itself, and the one hub the bar's tablet connects to. The
        // notifier only holds IHubContext, which is itself a singleton — no
        // DbContext, no per-request state.
        services.AddSignalR();
        services.AddSingleton<IKdsBoardNotifier, KdsBoardNotifier>();
        services.AddSingleton<ITillNotifier, TillNotifier>();
        services.AddSingleton<IOrderFollowers, OrderFollowers>();

        // Registered as a collection on purpose: the handler picks the strategy
        // that matches the method asked for, so adding cash or VIP balance is
        // adding a class here and touching nothing else.
        services.AddScoped<IPaymentStrategy, DigitalPaymentStrategy>();
        services.AddScoped<IPaymentStrategy, CashPaymentStrategy>();

        return services;
    }

    /// <summary>
    /// The only way to reach BootstrapSeeder from outside this assembly: it
    /// stays internal, and this is the door.
    /// </summary>
    public static Task SeedBootstrapDataAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<BootstrapSeeder>().SeedAsync(cancellationToken);
}
