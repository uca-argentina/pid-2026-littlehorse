using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Persistence;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>US-35, criteria 2 and 3: which night an order confirmed now belongs to, if any.</summary>
[Collection(nameof(SqlServerCollection))]
public sealed class UnderwayNightLookupTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    [Theory]
    [InlineData(0, true)]
    [InlineData(3 * 60, true)]
    [InlineData(-1, false)]
    [InlineData(7 * 60, false)] // closing is exclusive: 06:00 sharp takes no orders
    public async Task FindIdAsync_ForAMomentAroundTheHours_FindsTheNightOnlyWhileItIsOn(int minutesAfterOpening, bool found)
    {
        (Venue venue, Night night) = await SeedVenueWithANight();
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        Guid? underway = await new UnderwayNightLookup(context).FindIdAsync(Opening.AddMinutes(minutesAfterOpening), CancellationToken.None);

        Assert.Equal(found ? night.Id : null, underway);
    }

    // The most important invariant of the data model, again: another venue's
    // night does not open this venue for orders.
    [Fact]
    public async Task FindIdAsync_WhenOnlyAnotherVenueHasANightOn_FindsNothing()
    {
        (Venue mine, _) = await SeedVenueWithANight(withNight: false);
        await SeedVenueWithANight();

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.Null(await new UnderwayNightLookup(asMine).FindIdAsync(Opening.AddHours(1), CancellationToken.None));
    }

    private async Task<(Venue Venue, Night Night)> SeedVenueWithANight(bool withNight = true)
    {
        Venue venue = Venue.Create("Bar", $"bar-{Guid.NewGuid():N}");
        StaffUser[] crew =
        [
            StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds),
            StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier),
        ];
        Night night = Night.Create(venue.Id, "Saturday", Opening, Closing, crew);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.StaffUsers.AddRange(crew);
        if (withNight) seed.Nights.Add(night);
        await seed.SaveChangesAsync();

        return (venue, night);
    }
}
