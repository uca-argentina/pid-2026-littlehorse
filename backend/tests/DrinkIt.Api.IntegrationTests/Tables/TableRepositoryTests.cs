using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Tables;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Tables;

/// <summary>
/// A table number is taken inside one venue and free in every other one: two
/// venues each have their own "Mesa 5". The handler asks before inserting, but
/// that check is only worth as much as the index behind it.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class TableRepositoryTests(SqlServerFixture sql)
{
    [Fact]
    public async Task NumberExistsAsync_WhenThisVenueHasIt_IsTrue()
    {
        (Venue mine, _) = await SeedTwoVenues(mineNumber: 5, theirsNumber: 5);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.True(await new TableRepository(asMine).NumberExistsAsync(5, CancellationToken.None));
    }

    [Fact]
    public async Task NumberExistsAsync_WhenOnlyAnotherVenueHasIt_IsFalse()
    {
        (Venue mine, _) = await SeedTwoVenues(mineNumber: 4, theirsNumber: 5);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.False(await new TableRepository(asMine).NumberExistsAsync(5, CancellationToken.None));
    }

    // Put away does not free the number: orders already placed say "Mesa 5",
    // and a new table 5 would make them ambiguous.
    [Fact]
    public async Task NumberExistsAsync_WhenTheTableWithThatNumberIsPutAway_IsStillTrue()
    {
        (Venue mine, _) = await SeedTwoVenues(mineNumber: 5, theirsNumber: 6);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Table own = await asMine.Tables.SingleAsync();
        own.Deactivate();
        await asMine.SaveChangesAsync();

        Assert.True(await new TableRepository(asMine).NumberExistsAsync(5, CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenAnotherVenueAlreadyHasThatNumber_StillCreatesTheTable()
    {
        (Venue mine, _) = await SeedTwoVenues(mineNumber: 4, theirsNumber: 5);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        TableRepository repository = new(asMine);

        await repository.AddAsync(Table.Create(mine.Id, 5, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(await repository.NumberExistsAsync(5, CancellationToken.None));
    }

    // The index is the last line of defence: two administrators saving the same
    // new table at once both pass the check.
    [Fact]
    public async Task AddAsync_WhenThisVenueAlreadyHasThatNumber_IsRefusedByTheDatabase()
    {
        (Venue mine, _) = await SeedTwoVenues(mineNumber: 5, theirsNumber: 6);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        await Assert.ThrowsAsync<DbUpdateException>(() => new TableRepository(asMine).AddAsync(
            Table.Create(mine.Id, 5, DateTimeOffset.UtcNow),
            CancellationToken.None));
    }

    [Fact]
    public async Task GetForUpdateAsync_WhenThisVenueHasIt_ReturnsItTracked()
    {
        (Venue mine, _) = await SeedTwoVenues(mineNumber: 5, theirsNumber: 6);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Guid id = (await asMine.Tables.AsNoTracking().SingleAsync()).Id;

        Table? found = await new TableRepository(asMine).GetForUpdateAsync(id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(5, found.Number);
    }

    // Another venue's id is not found, not forbidden: an administrator cannot
    // put away, or even confirm the existence of, the table next door.
    [Fact]
    public async Task GetForUpdateAsync_WhenItBelongsToAnotherVenue_IsNull()
    {
        (Venue mine, Venue theirs) = await SeedTwoVenues(mineNumber: 5, theirsNumber: 5);

        await using DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id);
        Guid foreign = (await asTheirs.Tables.SingleAsync()).Id;

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.Null(await new TableRepository(asMine).GetForUpdateAsync(foreign, CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenATableWasPutAway_KeepsItPutAway()
    {
        (Venue mine, _) = await SeedTwoVenues(mineNumber: 5, theirsNumber: 6);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        TableRepository repository = new(asMine);
        Guid id = (await asMine.Tables.AsNoTracking().SingleAsync()).Id;

        Table table = (await repository.GetForUpdateAsync(id, CancellationToken.None))!;
        table.Deactivate();
        await repository.SaveChangesAsync(CancellationToken.None);

        await using DrinkItDbContext reopened = sql.CreateContext(mine.Id);

        Assert.False((await reopened.Tables.SingleAsync()).IsActive);
    }

    private async Task<(Venue Mine, Venue Theirs)> SeedTwoVenues(int mineNumber, int theirsNumber)
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.Tables.Add(Table.Create(mine.Id, mineNumber, DateTimeOffset.UtcNow));
        seed.Tables.Add(Table.Create(theirs.Id, theirsNumber, DateTimeOffset.UtcNow));
        await seed.SaveChangesAsync();

        return (mine, theirs);
    }
}
