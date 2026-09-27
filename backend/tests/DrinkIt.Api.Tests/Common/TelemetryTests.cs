using Azure.Monitor.OpenTelemetry.AspNetCore;
using DrinkIt.Api.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// Application Insights only exists where Azure hands over a connection string.
/// Asserted rather than trusted: a missing wire shows up as an empty Application
/// Insights resource and nothing else, which is how it went unnoticed the first time.
/// </summary>
public class TelemetryTests
{
    private const string ConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://ingest.example.net/";

    [Fact]
    public void AddTelemetry_WhenAzureSetsAConnectionString_SendsToThatResource()
    {
        ServiceCollection services = Configured(ConnectionString);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Contains(services, service => service.ServiceType == typeof(TracerProvider));
        Assert.Equal(ConnectionString, provider.GetRequiredService<IOptions<AzureMonitorOptions>>().Value.ConnectionString);
    }

    // Development and the tests have no resource to send to, and the Azure
    // Monitor distro throws at startup without a connection string.
    [Fact]
    public void AddTelemetry_WhenThereIsNoConnectionString_RegistersNothing()
    {
        ServiceCollection services = Configured(connectionString: null);

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(TracerProvider));
    }

    private static ServiceCollection Configured(string? connectionString)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("ApplicationInsights:ConnectionString", connectionString)])
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddTelemetry(configuration);

        return services;
    }
}
