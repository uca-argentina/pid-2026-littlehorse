using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Staff;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-35, criterion 1: two nights of a venue never overlap. The handler asks
/// before inserting, and the question is only worth as much as the venue it is
/// asked about.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightRepositoryTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    [Fact]
    public async Task AddAsync_WhenTheNightIsValid_KeepsItsHoursAndItsCrew()
    {
        (Venue mine, StaffUser[] crew, _) = await SeedTwoVenues();

        await using (DrinkItDbContext write = sql.CreateContext(mine.Id))
        {
            await new NightRepository(write).AddAsync(Night.Create(mine.Id, "Saturday", Opening, Closing, crew), CancellationToken.None);
        }

        await using DrinkItDbContext read = sql.CreateContext(mine.Id);
        Night stored = await read.Nights.SingleAsync();

        Assert.Equal("Saturday", stored.Name);
        Assert.Equal(Opening, stored.StartsAt);
        Assert.Equal(Closing, stored.EndsAt);
        Assert.Equal(crew.Select(member => member.Id).Order(), stored.CrewIds.Order());
    }

    // The crew lives in a JSON column: changing the list in place has to reach
    // the database, not only the tracked object.
    [Fact]
    public async Task SaveChangesAsync_AfterAnEdit_KeepsTheNewNameHoursAndCrew()
    {
        (Venue mine, StaffUser[] crew, _) = await SeedTwoVenues();
        StaffUser waiter = StaffUser.Create(mine.Id, "martin", "hash", StaffRole.Waiter);
        Night night = Night.Create(mine.Id, "Saturday", Opening, Closing, crew);

        await using (DrinkItDbContext seed = sql.CreateContext(mine.Id))
        {
            seed.StaffUsers.Add(waiter);
            await new NightRepository(seed).AddAsync(night, CancellationToken.None);
        }

        await using (DrinkItDbContext edit = sql.CreateContext(mine.Id))
        {
            NightRepository repository = new(edit);
            Night tracked = (await repository.GetForUpdateAsync(night.Id, CancellationToken.None))!;
            tracked.Update("Saturday, late", Opening, Closing.AddHours(1), [.. crew, waiter], Opening.AddHours(-1));
            await repository.SaveChangesAsync(CancellationToken.None);
        }

        await using DrinkItDbContext read = sql.CreateContext(mine.Id);
        Night stored = await read.Nights.SingleAsync(n => n.Id == night.Id);

        Assert.Equal("Saturday, late", stored.Name);
        Assert.Equal(Closing.AddHours(1), stored.EndsAt);
        Assert.Contains(waiter.Id, stored.CrewIds);
    }

    [Fact]
    public async Task OverlapsAsync_WhenTheOnlyNightInTheWayIsTheOneExcluded_IsFalse()
    {
        (Venue mine, StaffUser[] crew, _) = await SeedTwoVenues();
        Night night = Night.Create(mine.Id, "Saturday", Opening, Closing, crew);
        await using DrinkItDbContext context = sql.CreateContext(mine.Id);
        NightRepository repository = new(context);
        await repository.AddAsync(night, CancellationToken.None);

        Assert.False(await repository.OverlapsAsync(Opening, Closing.AddHours(1), night.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(-2, 1, true)] // starts before, ends inside
    [InlineData(6, 9, true)] // starts inside, ends after
    [InlineData(1, 2, true)] // inside
    [InlineData(-1, 8, true)] // wraps it
    [InlineData(-5, 0, false)] // ends right when it starts
    [InlineData(7, 12, false)] // starts right when it ends
    public async Task OverlapsAsync_ForHoursAroundAStoredNight_TellsWhetherTheyShareAMoment(int fromHour, int toHour, bool expected)
    {
        (Venue mine, StaffUser[] crew, _) = await SeedTwoVenues();
        await using DrinkItDbContext context = sql.CreateContext(mine.Id);
        NightRepository repository = new(context);
        await repository.AddAsync(Night.Create(mine.Id, "Saturday", Opening, Closing, crew), CancellationToken.None);

        bool overlaps = await repository.OverlapsAsync(Opening.AddHours(fromHour), Opening.AddHours(toHour), excluding: null, CancellationToken.None);

        Assert.Equal(expected, overlaps);
    }

    // Two venues open the same Saturday without meeting.
    [Fact]
    public async Task OverlapsAsync_WhenOnlyAnotherVenueHasANightThen_IsFalse()
    {
        (Venue mine, _, (Venue theirs, StaffUser[] theirCrew)) = await SeedTwoVenues();

        await using (DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id))
        {
            await new NightRepository(asTheirs).AddAsync(Night.Create(theirs.Id, "Saturday", Opening, Closing, theirCrew), CancellationToken.None);
        }

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.False(await new NightRepository(asMine).OverlapsAsync(Opening, Closing, excluding: null, CancellationToken.None));
    }

    [Fact]
    public async Task Nights_WhenReadFromAnotherVenue_AreNotVisible()
    {
        (Venue mine, _, (Venue theirs, StaffUser[] theirCrew)) = await SeedTwoVenues();

        await using (DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id))
        {
            await new NightRepository(asTheirs).AddAsync(Night.Create(theirs.Id, "Saturday", Opening, Closing, theirCrew), CancellationToken.None);
        }

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.Empty(await asMine.Nights.ToListAsync());
    }

    // What the handler reads the crew with: an id from another venue is
    // missing, just like a made-up one.
    [Fact]
    public async Task ListByIdsAsync_WhenAnIdBelongsToAnotherVenue_LeavesItOut()
    {
        (Venue mine, StaffUser[] crew, (_, StaffUser[] theirCrew)) = await SeedTwoVenues();

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        IReadOnlyList<StaffUser> found = await new StaffUserRepository(asMine)
            .ListByIdsAsync([crew[0].Id, theirCrew[0].Id], CancellationToken.None);

        Assert.Equal([crew[0].Id], found.Select(member => member.Id));
    }

    private async Task<(Venue Mine, StaffUser[] Crew, (Venue Venue, StaffUser[] Crew) Theirs)> SeedTwoVenues()
    {
        Venue mine = Venue.Create("Bar Mine", UniqueSlug());
        Venue theirs = Venue.Create("Bar Theirs", UniqueSlug());
        StaffUser[] crew = CrewOf(mine);
        StaffUser[] theirCrew = CrewOf(theirs);

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.StaffUsers.AddRange([.. crew, .. theirCrew]);
        await seed.SaveChangesAsync();

        return (mine, crew, (theirs, theirCrew));
    }

    private static StaffUser[] CrewOf(Venue venue) =>
    [
        StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds),
        StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier),
    ];

    /// <summary>Guid.NewGuid and not CreateVersion7, for the reason VenueIsolationTests gives.</summary>
    private static string UniqueSlug() => $"bar-{Guid.NewGuid():N}";
}
