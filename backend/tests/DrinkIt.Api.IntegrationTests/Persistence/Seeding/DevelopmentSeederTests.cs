using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Persistence.Seeding;
using DrinkIt.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkIt.Api.IntegrationTests.Persistence.Seeding;

[Collection(nameof(SqlServerCollection))]
public sealed class DevelopmentSeederTests(SqlServerFixture sql)
{
    private static readonly DevelopmentSeedOptions SeedOptions = new()
    {
        VenueSlug = $"seed-{Guid.NewGuid():N}",
        AdminUsername = "admin",
        AdminPassword = "a long and decent password",
    };

    [Fact]
    public async Task SeedAsync_WhenNothingExists_CreatesOneVenueAndOneAdministrator()
    {
        await using DrinkItDbContext context = sql.CreateContext(Guid.Empty);

        await Seeder(context).SeedAsync(CancellationToken.None);

        Venue venue = await context.Venues.SingleAsync(v => v.Slug == SeedOptions.VenueSlug);
        StaffUser admin = await context.StaffUsers
            .IgnoreQueryFilters()
            .SingleAsync(u => u.VenueId == venue.Id);

        Assert.Equal("admin", admin.Username);
        Assert.Equal(StaffRole.Administrator, admin.Role);
        Assert.True(new IdentityPasswordHasher().Verify(SeedOptions.AdminPassword, admin.PasswordHash));
    }

    // The seeder runs on every Development start, so it must be safe to call
    // twice: once for the first ever run, and once for every restart after.
    [Fact]
    public async Task SeedAsync_WhenTheAdminAlreadyExists_DoesNotCreateASecondOne()
    {
        await using DrinkItDbContext first = sql.CreateContext(Guid.Empty);
        await Seeder(first).SeedAsync(CancellationToken.None);

        await using DrinkItDbContext second = sql.CreateContext(Guid.Empty);
        await Seeder(second).SeedAsync(CancellationToken.None);

        await using DrinkItDbContext verify = sql.CreateContext(Guid.Empty);
        int venueCount = await verify.Venues.CountAsync(v => v.Slug == SeedOptions.VenueSlug);
        int adminCount = await verify.StaffUsers.IgnoreQueryFilters()
            .CountAsync(u => u.Username == "admin" && u.VenueId != Guid.Empty
                && verify.Venues.Any(v => v.Id == u.VenueId && v.Slug == SeedOptions.VenueSlug));

        Assert.Equal(1, venueCount);
        Assert.Equal(1, adminCount);
    }

    [Fact]
    public async Task SeedAsync_WhenThePasswordIsBlank_ThrowsRatherThanSeedingAGuessableAccount()
    {
        await using DrinkItDbContext context = sql.CreateContext(Guid.Empty);
        DevelopmentSeeder seeder = new(
            context,
            new IdentityPasswordHasher(),
            Options.Create(new DevelopmentSeedOptions { AdminPassword = "" }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync(CancellationToken.None));
    }

    private static DevelopmentSeeder Seeder(DrinkItDbContext context) =>
        new(context, new IdentityPasswordHasher(), Options.Create(SeedOptions));
}
