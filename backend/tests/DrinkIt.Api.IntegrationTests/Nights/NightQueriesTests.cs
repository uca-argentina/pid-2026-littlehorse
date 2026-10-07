using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Persistence;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>US-35, criterion 1: the night "queda en el listado".</summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightQueriesTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 23, 0, 0, TimeSpan.FromHours(-3));

    // The next night, or the one on now, is what the administrator came for.
    [Fact]
    public async Task ListAsync_WhenTheVenueHasSeveralNights_ShowsTheLatestFirstWithItsCrew()
    {
        (Venue mine, StaffUser[] crew) = await SeedVenue();
        await using DrinkItDbContext context = sql.CreateContext(mine.Id);
        context.Nights.AddRange(
            Night.Create(mine.Id, "Friday", Friday, Friday.AddHours(7), crew),
            Night.Create(mine.Id, "Saturday", Friday.AddDays(1), Friday.AddDays(1).AddHours(7), crew));
        await context.SaveChangesAsync();

        IReadOnlyList<NightSummary> listed = await new NightQueries(context).ListAsync(CancellationToken.None);

        Assert.Equal(["Saturday", "Friday"], listed.Select(night => night.Name));
        Assert.Equal(crew.Select(member => member.Id).Order(), listed[0].CrewIds.Order());
    }

    [Fact]
    public async Task ListAsync_WhenAnotherVenueHasNights_LeavesThemOut()
    {
        (Venue mine, _) = await SeedVenue();
        (Venue theirs, StaffUser[] theirCrew) = await SeedVenue();

        await using (DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id))
        {
            asTheirs.Nights.Add(Night.Create(theirs.Id, "Friday", Friday, Friday.AddHours(7), theirCrew));
            await asTheirs.SaveChangesAsync();
        }

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.Empty(await new NightQueries(asMine).ListAsync(CancellationToken.None));
    }

    private async Task<(Venue Venue, StaffUser[] Crew)> SeedVenue()
    {
        Venue venue = Venue.Create("Bar", $"bar-{Guid.NewGuid():N}");
        StaffUser[] crew =
        [
            StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds),
            StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier),
        ];

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.StaffUsers.AddRange(crew);
        await seed.SaveChangesAsync();

        return (venue, crew);
    }
}
