using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Authentication;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Persistence;

/// <summary>
/// CLAUDE.md calls this the most important invariant of the data model: data
/// from one venue must never be visible from another. Everything else about
/// multi-tenancy is bookkeeping; this is the part that leaks real data.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class VenueIsolationTests(SqlServerFixture sql)
{
    [Fact]
    public async Task StaffUsers_WhenReadFromAnotherVenue_AreNotVisible()
    {
        (Venue mine, Venue theirs) = await SeedTwoVenuesWithOneUserEach("euge", "nico");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        string[] visible = await asMine.StaffUsers.Select(user => user.Username).ToArrayAsync();

        // Scoped to the two venues this test just created, not a table-wide
        // count: the database is shared across every test class in
        // SqlServerCollection, so other tests' rows are expected to be there.
        int storedForOurVenues = await asMine.StaffUsers
            .IgnoreQueryFilters()
            .CountAsync(u => u.VenueId == mine.Id || u.VenueId == theirs.Id);

        // Both of our rows exist, so the assertion below is about the filter
        // and not about the seed having silently failed.
        Assert.Equal(2, storedForOurVenues);
        Assert.Equal(["euge"], visible);
        Assert.NotEqual(mine.Id, theirs.Id);
    }

    // The query login actually runs. It relies on the global filter rather than
    // naming the venue itself, so this is what catches somebody adding
    // IgnoreQueryFilters to it.
    [Fact]
    public async Task StaffCredentialsQuery_WhenTheUserBelongsToAnotherVenue_FindsNothing()
    {
        (Venue mine, _) = await SeedTwoVenuesWithOneUserEach("ana", "beto");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        StaffCredentialsQuery query = new(asMine);

        StaffCredentials? own = await query.FindAsync("ana", CancellationToken.None);
        StaffCredentials? foreign = await query.FindAsync("beto", CancellationToken.None);

        Assert.NotNull(own);
        Assert.Null(foreign);
    }

    private async Task<(Venue Mine, Venue Theirs)> SeedTwoVenuesWithOneUserEach(
        string mineUsername,
        string theirsUsername)
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", UniqueSlug());
        Venue theirs = Venue.Create("Bar Theirs", UniqueSlug());

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.StaffUsers.AddRange(
            StaffUser.Create(mine.Id, mineUsername, "hash", StaffRole.Administrator),
            StaffUser.Create(theirs.Id, theirsUsername, "hash", StaffRole.Bartender));
        await seed.SaveChangesAsync();

        return (mine, theirs);
    }

    /// <summary>
    /// Guid.NewGuid and not CreateVersion7: v7 puts the millisecond timestamp in
    /// its leading bits, so two of them made in the same millisecond share a
    /// prefix. Truncating one throws away exactly the part that makes it unique.
    /// Nothing is truncated here either, since 36 characters fit the slug limit.
    /// </summary>
    private static string UniqueSlug() => $"bar-{Guid.NewGuid():N}";
}
