using DrinkIt.Application.Common;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace DrinkIt.Api.IntegrationTests.Persistence;

/// <summary>
/// This the most important invariant of the data model: an order
/// or a user from one venue must never be visible from another. Everything else
/// about multi-tenancy is bookkeeping; this is the part that leaks real data.
/// </summary>
public sealed class VenueIsolationTests : IAsyncLifetime
{
    // Same tag as docker-compose.yml so both reuse one pulled image.
    private readonly MsSqlContainer _sqlServer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _sqlServer.StartAsync();

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task StaffUsers_WhenReadFromAnotherVenue_AreNotVisible()
    {
        Venue alfa = Venue.Create("Bar Alfa", "bar-alfa");
        Venue beta = Venue.Create("Bar Beta", "bar-beta");
        StaffUser alfaUser = StaffUser.Create(alfa.Id, "euge", "hash", StaffRole.Administrator);
        StaffUser betaUser = StaffUser.Create(beta.Id, "nico", "hash", StaffRole.Bartender);

        await using (DrinkItDbContext seed = CreateContext(alfa.Id))
        {
            await seed.Database.MigrateAsync();
            seed.Venues.AddRange(alfa, beta);
            seed.StaffUsers.AddRange(alfaUser, betaUser);
            await seed.SaveChangesAsync();
        }

        await using DrinkItDbContext asAlfa = CreateContext(alfa.Id);

        string[] visible = await asAlfa.StaffUsers.Select(user => user.Username).ToArrayAsync();
        int storedInTotal = await asAlfa.StaffUsers.IgnoreQueryFilters().CountAsync();

        // Both rows exist, so the assertion below is about the filter and not
        // about the seed having silently failed.
        Assert.Equal(2, storedInTotal);
        Assert.Equal(["euge"], visible);
    }

    private DrinkItDbContext CreateContext(Guid venueId)
    {
        DbContextOptions<DrinkItDbContext> options =
            new DbContextOptionsBuilder<DrinkItDbContext>()
                .UseSqlServer(_sqlServer.GetConnectionString())
                .Options;

        return new DrinkItDbContext(options, new FixedVenue(venueId));
    }

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id { get; } = id;
    }
}
