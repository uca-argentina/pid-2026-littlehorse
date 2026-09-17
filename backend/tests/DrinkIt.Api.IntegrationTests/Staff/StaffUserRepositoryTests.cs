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

    /// <summary>
    /// US-05: the row survives the baja. This is what the whole soft delete
    /// exists for — the orders that person prepared keep pointing somewhere.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_WhenSomebodyIsDeactivated_KeepsTheirRow()
    {
        (Venue mine, _) = await SeedTwoVenues("martin.p", "nobody");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        StaffUserRepository repository = new(asMine);

        StaffUser martin = (await repository.GetForUpdateAsync(
            await IdOf(asMine, "martin.p"), CancellationToken.None))!;
        martin.Deactivate();
        await repository.SaveChangesAsync(CancellationToken.None);

        await using DrinkItDbContext later = sql.CreateContext(mine.Id);
        StaffUser? stored = await later.StaffUsers.SingleOrDefaultAsync(u => u.Username == "martin.p");

        Assert.NotNull(stored);
        Assert.False(stored.IsActive);
        Assert.Equal("hash", stored.PasswordHash);
        Assert.Equal(StaffRole.Administrator, stored.Role);
    }

    /// <summary>
    /// The write side has to be scoped to the venue exactly like the read side.
    /// A repository that could load somebody else's aggregate would let one
    /// venue deactivate another venue's staff by guessing an id.
    /// </summary>
    [Fact]
    public async Task GetForUpdateAsync_WhenTheUserBelongsToAnotherVenue_FindsNothing()
    {
        (Venue mine, Venue theirs) = await SeedTwoVenues("ana", "beto");

        await using DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id);
        Guid betoId = await IdOf(asTheirs, "beto");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.Null(await new StaffUserRepository(asMine).GetForUpdateAsync(betoId, CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenSomebodyComesBack_TurnsTheirAccountBackOn()
    {
        (Venue mine, _) = await SeedTwoVenues("martin.p", "nobody");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        StaffUserRepository repository = new(asMine);
        Guid martinId = await IdOf(asMine, "martin.p");

        StaffUser martin = (await repository.GetForUpdateAsync(martinId, CancellationToken.None))!;
        martin.Deactivate();
        await repository.SaveChangesAsync(CancellationToken.None);

        martin.Activate();
        await repository.SaveChangesAsync(CancellationToken.None);

        await using DrinkItDbContext later = sql.CreateContext(mine.Id);

        Assert.True((await later.StaffUsers.SingleAsync(u => u.Id == martinId)).IsActive);
    }

    private static Task<Guid> IdOf(DrinkItDbContext context, string username) =>
        context.StaffUsers.AsNoTracking().Where(u => u.Username == username).Select(u => u.Id).SingleAsync();

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
