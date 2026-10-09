using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;

namespace DrinkIt.Api.IntegrationTests.Persistence;

/// <summary>
/// US-35, criterion 4: the KDS and the till only reach the venue's orders
/// while they work its night. Every test that calls them over HTTP or through a
/// hub starts with a venue, an account of that role, and a night on now that
/// lists it.
/// </summary>
internal static class SeedTonight
{
    /// <summary>A new venue whose night is on, with an account of that role in its crew.</summary>
    public static async Task<(Guid VenueId, Guid StaffUserId)> AnAccountWorkingTonight(SqlServerFixture sql, StaffRole role)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        await seed.SaveChangesAsync();

        return (venue.Id, await AnAccountWorkingTonight(sql, venue.Id, role));
    }

    /// <summary>
    /// An account of that role in an existing venue, on a night on now. The
    /// night always has a KDS and a cashier, as the domain requires, so a
    /// companion account fills whichever role the caller did not ask for.
    /// </summary>
    public static async Task<Guid> AnAccountWorkingTonight(SqlServerFixture sql, Guid venueId, StaffRole role)
    {
        StaffUser account = StaffUser.Create(venueId, $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..20], "hash", role);
        StaffUser companion = StaffUser.Create(
            venueId,
            $"companion-{Guid.NewGuid():N}"[..20],
            "hash",
            role == StaffRole.Kds ? StaffRole.Cashier : StaffRole.Kds);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using DrinkItDbContext seed = sql.CreateContext(venueId);
        seed.StaffUsers.AddRange(account, companion);
        seed.Nights.Add(Night.Create(venueId, "Tonight", now.AddHours(-1), now.AddHours(11), [account, companion]));
        await seed.SaveChangesAsync();

        return account.Id;
    }

    /// <summary>An account of that role in the venue that is in no night's crew.</summary>
    public static async Task<Guid> AnAccountOffTonight(SqlServerFixture sql, Guid venueId, StaffRole role)
    {
        StaffUser account = StaffUser.Create(venueId, $"off-{Guid.NewGuid():N}"[..20], "hash", role);

        await using DrinkItDbContext seed = sql.CreateContext(venueId);
        seed.StaffUsers.Add(account);
        await seed.SaveChangesAsync();

        return account.Id;
    }
}
