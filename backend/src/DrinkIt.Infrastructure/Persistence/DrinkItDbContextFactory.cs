using DrinkIt.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// Used only by "dotnet ef". It keeps migrations independent of how the API
/// wires its services, so generating one never depends on app configuration.
/// </summary>
internal sealed class DrinkItDbContextFactory : IDesignTimeDbContextFactory<DrinkItDbContext>
{
    private const string LocalConnectionString =
        "Server=localhost,1433;Database=drinkit;User Id=sa;Password=DrinkIt!Local1;TrustServerCertificate=True";

    public DrinkItDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("DRINKIT_DB") ?? LocalConnectionString;

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
