using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DrinkIt.Api.IntegrationTests.Common;

/// <summary>
/// The whole app — migrations, seeding, everything Program.cs does before it
/// starts listening — pointed at the Testcontainers instance the query tests
/// already run against, instead of the connection string in appsettings.json.
/// </summary>
internal sealed class DrinkItApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("ConnectionStrings:DrinkIt", connectionString),
        ]));
    }
}
