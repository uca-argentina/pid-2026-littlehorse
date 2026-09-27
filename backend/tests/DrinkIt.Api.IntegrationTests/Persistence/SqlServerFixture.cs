using System.Diagnostics.CodeAnalysis;
using DrinkIt.Application.Common;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.MsSql;

namespace DrinkIt.Api.IntegrationTests.Persistence;

/// <summary>
/// One SQL Server container per test class instead of one per test. Starting it
/// costs about thirty seconds, so sharing it is the difference between a suite
/// that runs and one nobody waits for.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // Same tag as docker-compose.yml so both reuse one pulled image.
    private readonly MsSqlContainer _sqlServer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();

        await using DrinkItDbContext context = CreateContext(Guid.Empty);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    /// <summary>A context scoped to one venue, as a real request would have.</summary>
    public DrinkItDbContext CreateContext(Guid venueId) => CreateContext(venueId, []);

    /// <summary>
    /// The same, with the interceptors a real request registers. Most tests do
    /// not want them: a stamp they did not ask for is one more thing to assert around.
    /// </summary>
    public DrinkItDbContext CreateContext(Guid venueId, params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<DrinkItDbContext>()
                .UseSqlServer(_sqlServer.GetConnectionString())
                .AddInterceptors(interceptors)
                .Options,
            new FixedVenue(venueId));

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id { get; } = id;
    }
}

/// <summary>
/// Puts every test class that needs SQL Server in the same xUnit collection, so
/// they share one SqlServerFixture instance instead of each IClassFixture
/// spinning up its own container.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit's own convention for a collection definition marker is "
        + "'<Name>Collection'; that is what [Collection(nameof(...))] expects to find.")]
[CollectionDefinition(nameof(SqlServerCollection))]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
