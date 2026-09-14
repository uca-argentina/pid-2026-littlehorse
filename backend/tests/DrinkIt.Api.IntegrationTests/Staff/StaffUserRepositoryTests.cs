using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Staff;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Staff;

/// <summary>
/// US-03, criteria 3 and 4: a username is taken inside one venue and free in
/// every other one. The handler asks before inserting, but that check is only
/// worth as much as the index behind it, and both have to agree on which venue
/// they are talking about.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class StaffUserRepositoryTests(SqlServerFixture sql)
{
    [Fact]
    public async Task UsernameExistsAsync_WhenSomebodyInThisVenueUsesIt_IsTrue()
    {
        (Venue mine, _) = await SeedTwoVenues("martin.p", "martin.p");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.True(await new StaffUserRepository(asMine).UsernameExistsAsync("martin.p", CancellationToken.None));
    }

    // Criterion 4. The row exists in the table, and this venue must not see it:
    // otherwise one venue hiring a "martin.p" would block every other venue.
    [Fact]
    public async Task UsernameExistsAsync_WhenOnlyAnotherVenueUsesIt_IsFalse()
    {
        (Venue mine, _) = await SeedTwoVenues("euge.q", "martin.p");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.False(await new StaffUserRepository(asMine).UsernameExistsAsync("martin.p", CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenAnotherVenueAlreadyHasThatUsername_StillCreatesTheUser()
    {
        (Venue mine, _) = await SeedTwoVenues("euge.q", "martin.p");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        StaffUserRepository repository = new(asMine);

        await repository.AddAsync(
            StaffUser.Create(mine.Id, "martin.p", "hash", StaffRole.Waiter),
            CancellationToken.None);

        Assert.True(await repository.UsernameExistsAsync("martin.p", CancellationToken.None));
    }

    /// <summary>
    /// The index is the last line of defence. Two administrators saving the same
    /// new username at once both pass the availability check, and only the
    /// database can stop the second one.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenThisVenueAlreadyHasThatUsername_IsRefusedByTheDatabase()
    {
        (Venue mine, _) = await SeedTwoVenues("martin.p", "euge.q");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        await Assert.ThrowsAsync<DbUpdateException>(() => new StaffUserRepository(asMine).AddAsync(
            StaffUser.Create(mine.Id, "martin.p", "hash", StaffRole.Waiter),
            CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_WhenAnotherVenueHasStaff_LeavesThemOut()
    {
        (Venue mine, _) = await SeedTwoVenues("euge.q", "martin.p");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        IReadOnlyList<StaffUserListItem> listed = await new StaffUserQueries(asMine)
            .ListAsync(CancellationToken.None);

        Assert.Equal(["euge.q"], listed.Select(user => user.Username));
    }

    // Deactivated staff stay in the listing: US-05 reactivates them from there,
    // and a row that disappears looks deleted, which is what the soft delete
    // exists to avoid.
    [Fact]
    public async Task ListAsync_WhenSomebodyWasDeactivated_StillShowsThemAfterTheActiveOnes()
    {
        (Venue mine, _) = await SeedTwoVenues("euge.q", "nobody");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        StaffUser left = StaffUser.Create(mine.Id, "andres.b", "hash", StaffRole.Kds);
        left.Deactivate();
        seed.StaffUsers.Add(left);
        await seed.SaveChangesAsync();

        IReadOnlyList<StaffUserListItem> listed = await new StaffUserQueries(seed)
            .ListAsync(CancellationToken.None);

        Assert.Equal(["euge.q", "andres.b"], listed.Select(user => user.Username));
        Assert.False(listed[1].IsActive);
    }

    private async Task<(Venue Mine, Venue Theirs)> SeedTwoVenues(string mineUsername, string theirsUsername)
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.StaffUsers.AddRange(
            StaffUser.Create(mine.Id, mineUsername, "hash", StaffRole.Administrator),
            StaffUser.Create(theirs.Id, theirsUsername, "hash", StaffRole.Kds));
        await seed.SaveChangesAsync();

        return (mine, theirs);
    }
}
