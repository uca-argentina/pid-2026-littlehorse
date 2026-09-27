using DrinkIt.Application.Security;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Persistence.Seeding;

/// <summary>
/// Creates one venue, its administrator and the three categories every venue
/// starts with, so there is somebody who can log in and a menu to load onto:
/// on a machine that just cloned the repo, and on the first production deploy,
/// where there is no sign-up. Runs on every start in every environment and does
/// nothing without a password, so production only sets one until the first
/// administrator exists.
/// </summary>
internal sealed class BootstrapSeeder(
    DrinkItDbContext context,
    IPasswordHasher passwordHasher,
    IOptions<BootstrapOptions> options,
    TimeProvider clock)
{
    /// <summary>What the platform started with, in the order the tabs are drawn.</summary>
    private static readonly string[] DefaultCategories = ["Tragos", "Cervezas", "Sin alcohol"];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        BootstrapOptions settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.AdminPassword)) return;

        // AsNoTracking + IgnoreQueryFilters: no venue is resolved yet outside a
        // request, and this must find an existing seed on every restart rather
        // than create a second one.
        Venue? venue = await context.Venues
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Slug == settings.VenueSlug, cancellationToken);

        bool venueIsNew = venue is null;

        venue ??= Venue.Create(settings.VenueName, settings.VenueSlug);

        string username = StaffUser.NormalizeUsername(settings.AdminUsername);

        bool adminExists = await context.StaffUsers
            .IgnoreQueryFilters()
            .AnyAsync(u => u.VenueId == venue.Id && u.Username == username, cancellationToken);

        bool hasCategories = await context.Categories
            .IgnoreQueryFilters()
            .AnyAsync(c => c.VenueId == venue.Id, cancellationToken);

        if (adminExists && hasCategories) return;

        if (venueIsNew) context.Venues.Add(venue);

        if (!adminExists)
        {
            context.StaffUsers.Add(StaffUser.Create(
                venue.Id,
                settings.AdminUsername,
                passwordHasher.Hash(settings.AdminPassword),
                StaffRole.Administrator));
        }

        if (!hasCategories)
        {
            DateTimeOffset now = clock.GetUtcNow();

            // A millisecond apart, so CreatedAt keeps the order they are listed in.
            for (int position = 0; position < DefaultCategories.Length; position++)
                context.Categories.Add(Category.Create(venue.Id, DefaultCategories[position], now.AddMilliseconds(position)));
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
