using Azure.Monitor.OpenTelemetry.AspNetCore;

namespace DrinkIt.Api.Common;

/// <summary>
/// Requests, exceptions, logs and the calls to SQL and Blob Storage, sent to
/// Application Insights through OpenTelemetry. The connection string comes from
/// configuration (ApplicationInsights:ConnectionString), which the Bicep sets on
/// the Container App: nothing here knows which resource it is.
/// </summary>
internal static class Telemetry
{
    public const string ConnectionStringKey = "ApplicationInsights:ConnectionString";

    public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration[ConnectionStringKey];

        // Only where Azure sets it: the distro throws at startup without a
        // connection string, and development has none.
        if (string.IsNullOrEmpty(connectionString)) return services;

        services.AddOpenTelemetry().UseAzureMonitor(options => options.ConnectionString = connectionString);

        return services;
    }
}
