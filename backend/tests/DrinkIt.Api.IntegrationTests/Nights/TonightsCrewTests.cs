using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Persistence;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-35, criterion 4: whose screens show the venue's orders. The night that
/// counts is the last one that started, not only the one on: paid orders are
/// still made and handed over after closing, so Saturday's bar keeps its queue
/// past 06:00, until Sunday's night begins.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class TonightsCrewTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Saturday = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Sunday = Saturday.AddDays(1);

    [Fact]
    public async Task IncludesAsync_WhenTheAccountWorksTheNightOn_IsTrue()
    {
        (Venue venue, StaffUser saturdayBar, _, _) = await SeedTwoNights();
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        Assert.True(await new TonightsCrew(context).IncludesAsync(saturdayBar.Id, Saturday.AddHours(2), CancellationToken.None));
    }

    [Fact]
    public async Task IncludesAsync_WhenTheAccountIsNotInTheNightOn_IsFalse()
    {
        (Venue venue, _, StaffUser sundayBar, _) = await SeedTwoNights();
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        Assert.False(await new TonightsCrew(context).IncludesAsync(sundayBar.Id, Saturday.AddHours(2), CancellationToken.None));
    }

    // "Termina la noche con pedidos pagos sin entregar: se siguen preparando."
    [Fact]
    public async Task IncludesAsync_AfterTheNightEndedAndBeforeTheNextStarts_StillCountsItsCrew()
    {
        (Venue venue, StaffUser saturdayBar, _, _) = await SeedTwoNights();
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        Assert.True(await new TonightsCrew(context).IncludesAsync(saturdayBar.Id, Saturday.AddHours(10), CancellationToken.None));
    }

    [Fact]
    public async Task IncludesAsync_OnceTheNextNightStarted_CountsOnlyItsCrew()
    {
        (Venue venue, StaffUser saturdayBar, StaffUser sundayBar, _) = await SeedTwoNights();
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        TonightsCrew crew = new(context);

        Assert.False(await crew.IncludesAsync(saturdayBar.Id, Sunday.AddHours(1), CancellationToken.None));
        Assert.True(await crew.IncludesAsync(sundayBar.Id, Sunday.AddHours(1), CancellationToken.None));
    }

    // A venue that never set up a night has nobody working one.
    [Fact]
    public async Task IncludesAsync_BeforeAnyNightStarted_IsFalse()
    {
        (Venue venue, StaffUser saturdayBar, _, _) = await SeedTwoNights();
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);

        Assert.False(await new TonightsCrew(context).IncludesAsync(saturdayBar.Id, Saturday.AddHours(-1), CancellationToken.None));
    }

    // The account and its night are another venue's: this venue has no night.
    [Fact]
    public async Task IncludesAsync_WhenOnlyAnotherVenueHasANight_IsFalse()
    {
        (_, StaffUser theirBar, _, _) = await SeedTwoNights();
        (Venue mine, _, _, _) = await SeedTwoNights(withNights: false);
        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.False(await new TonightsCrew(asMine).IncludesAsync(theirBar.Id, Saturday.AddHours(2), CancellationToken.None));
    }

    /// <summary>Saturday 23:00–06:00 and Sunday 23:00–06:00, each with its own bar and a shared till.</summary>
    private async Task<(Venue Venue, StaffUser SaturdayBar, StaffUser SundayBar, StaffUser Till)> SeedTwoNights(bool withNights = true)
    {
        Venue venue = Venue.Create("Bar", $"bar-{Guid.NewGuid():N}");
        StaffUser saturdayBar = StaffUser.Create(venue.Id, "saturday-bar", "hash", StaffRole.Kds);
        StaffUser sundayBar = StaffUser.Create(venue.Id, "sunday-bar", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.StaffUsers.AddRange(saturdayBar, sundayBar, till);

        if (withNights)
        {
            seed.Nights.AddRange(
                Night.Create(venue.Id, "Saturday", Saturday, Saturday.AddHours(7), [saturdayBar, till]),
                Night.Create(venue.Id, "Sunday", Sunday, Sunday.AddHours(7), [sundayBar, till]));
        }

        await seed.SaveChangesAsync();

        return (venue, saturdayBar, sundayBar, till);
    }
}
