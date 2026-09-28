using DrinkIt.Api.Features.Authentication;
using DrinkIt.Api.Features.Menu;
using DrinkIt.Api.Features.Orders;
using DrinkIt.Api.Features.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// Handlers are registered by hand, one feature at a time, on purpose: no
/// assembly scanning. The price of doing it by hand is forgetting one, which
/// otherwise surfaces as a failure on the first request that needs it.
/// </summary>
public class HandlerRegistrationTests
{
    [Fact]
    public void FeatureRegistrations_WhenAllAreAdded_RegisterEveryApplicationHandlerOncePerRequest()
    {
        ServiceCollection services = new();
        services.AddLoginHandlers();
        services.AddStaffHandlers();
        services.AddMenuHandlers();
        services.AddOrderHandlers();

        string[] missing =
        [
            .. typeof(Application.AssemblyMarker).Assembly.GetExportedTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
                .Where(handler => !services.Any(service =>
                    service.ServiceType == handler && service.Lifetime == ServiceLifetime.Scoped))
                .Select(handler => handler.Name)
                .Order(StringComparer.Ordinal)
        ];

        Assert.True(
            missing.Length == 0,
            $"These handlers are not registered as scoped by any feature: {string.Join(", ", missing)}");
    }
}
