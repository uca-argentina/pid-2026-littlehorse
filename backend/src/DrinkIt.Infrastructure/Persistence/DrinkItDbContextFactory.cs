using DrinkIt.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// Used by "dotnet ef" to construct a DbContext for migrations.
/// </summary>
/// <remarks>
/// Not strictly required: without this, EF's tooling falls back to building
/// the whole app through Program.cs and pulling the context from its real DI
/// container, which does work today. This factory exists so migrating never
/// depends on that succeeding — Program.cs wires JWT, password hashing and
/// whatever else the app needs, none of which has anything to do with the
/// schema, and any of it failing (say, ASPNETCORE_ENVIRONMENT being unset,
/// which trips our JWT signing-key guard) would otherwise block migrations too.
///
/// Reads DrinkIt.Api/appsettings.json instead of holding its own copy of the
/// connection string, so there is exactly one place — the same one the running
/// API reads — that says where the local database is.
/// </remarks>
internal sealed class DrinkItDbContextFactory : IDesignTimeDbContextFactory<DrinkItDbContext>
{
    public DrinkItDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "DrinkIt.Api"))
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        string connectionString = configuration.GetConnectionString("DrinkIt")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DrinkIt is missing. Check DrinkIt.Api/appsettings.json.");

        DbContextOptions<DrinkItDbContext> options =
            new DbContextOptionsBuilder<DrinkItDbContext>()
                .UseSqlServer(connectionString)
                .Options;

        // Migrations describe the schema, which is the same for every venue.
        return new DrinkItDbContext(options, new NoVenue());
    }

    private sealed class NoVenue : ICurrentVenue
    {
        public Guid Id => Guid.Empty;
    }
}
