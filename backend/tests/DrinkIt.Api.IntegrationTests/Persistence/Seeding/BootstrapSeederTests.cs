using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Persistence.Seeding;
using DrinkIt.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkIt.Api.IntegrationTests.Persistence.Seeding;

[Collection(nameof(SqlServerCollection))]
public sealed class BootstrapSeederTests(SqlServerFixture sql)
{
    private static readonly BootstrapOptions SeedOptions = new()
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

    // The seeder runs on every start, so it must be safe to call
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

    // A new venue is born with the three tabs the menu has always had, in the
    // order they are drawn, so there is something to load products onto.
    [Fact]
    public async Task SeedAsync_WhenNothingExists_CreatesTheThreeStartingCategoriesInOrder()
    {
        await using DrinkItDbContext context = sql.CreateContext(Guid.Empty);

        await Seeder(context).SeedAsync(CancellationToken.None);

        Venue venue = await context.Venues.SingleAsync(v => v.Slug == SeedOptions.VenueSlug);
        List<string> names = await context.Categories
            .IgnoreQueryFilters()
            .Where(c => c.VenueId == venue.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.Name)
            .ToListAsync();

        Assert.Equal(["Tragos", "Cervezas", "Sin alcohol"], names);
    }

    [Fact]
    public async Task SeedAsync_WhenItRunsAgain_DoesNotDuplicateTheCategories()
    {
        await using DrinkItDbContext first = sql.CreateContext(Guid.Empty);
        await Seeder(first).SeedAsync(CancellationToken.None);

        await using DrinkItDbContext second = sql.CreateContext(Guid.Empty);
        await Seeder(second).SeedAsync(CancellationToken.None);

        await using DrinkItDbContext verify = sql.CreateContext(Guid.Empty);
        Venue venue = await verify.Venues.SingleAsync(v => v.Slug == SeedOptions.VenueSlug);

        Assert.Equal(3, await verify.Categories.IgnoreQueryFilters().CountAsync(c => c.VenueId == venue.Id));
    }

    // The seeder runs on every start in every environment, and production only
    // configures a password for the first deploy. Without one there is nothing
    // to seed, and seeding an account with a guessable password is worse.
    [Fact]
    public async Task SeedAsync_WhenThePasswordIsBlank_SeedsNothing()
    {
        string venueSlug = $"seed-{Guid.NewGuid():N}";
        await using DrinkItDbContext context = sql.CreateContext(Guid.Empty);
        BootstrapSeeder seeder = new(
            context,
            new IdentityPasswordHasher(),
            Options.Create(new BootstrapOptions { VenueSlug = venueSlug, AdminPassword = "" }),
            TimeProvider.System);

        await seeder.SeedAsync(CancellationToken.None);

        Assert.False(await context.Venues.AnyAsync(v => v.Slug == venueSlug));
    }

    private static BootstrapSeeder Seeder(DrinkItDbContext context) =>
        new(context, new IdentityPasswordHasher(), Options.Create(SeedOptions), TimeProvider.System);
}
