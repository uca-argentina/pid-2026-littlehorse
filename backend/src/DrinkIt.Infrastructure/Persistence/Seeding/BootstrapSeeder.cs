using DrinkIt.Application.Security;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Persistence.Seeding;

/// <summary>
/// Creates one venue and one administrator so there is somebody who can log in:
/// on a machine that just cloned the repo, and on the first production deploy,
/// where there is no sign-up. Runs on every start in every environment and does
/// nothing without a password, so production only sets one until the first
/// administrator exists.
/// </summary>
internal sealed class BootstrapSeeder(
    DrinkItDbContext context,
    IPasswordHasher passwordHasher,
    IOptions<BootstrapOptions> options)
{
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

        venue ??= Venue.Create(settings.VenueName, settings.VenueSlug);

        string username = StaffUser.NormalizeUsername(settings.AdminUsername);

        bool adminExists = await context.StaffUsers
            .IgnoreQueryFilters()
            .AnyAsync(u => u.VenueId == venue.Id && u.Username == username, cancellationToken);

        if (adminExists) return;

        if (context.Entry(venue).State == EntityState.Detached) context.Venues.Add(venue);

        StaffUser admin = StaffUser.Create(
            venue.Id,
            settings.AdminUsername,
            passwordHasher.Hash(settings.AdminPassword),
            StaffRole.Administrator);

        context.StaffUsers.Add(admin);

        await context.SaveChangesAsync(cancellationToken);
    }
}
