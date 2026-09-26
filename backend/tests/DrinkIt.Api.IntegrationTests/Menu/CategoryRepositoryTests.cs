using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Menu;

/// <summary>
/// A category name is taken inside one venue and free in every other one, same
/// as a product's. The handler asks before inserting, but that check is only
/// worth as much as the index behind it.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class CategoryRepositoryTests(SqlServerFixture sql)
{
    [Fact]
    public async Task NameExistsAsync_WhenThisVenueHasIt_IsTrue()
    {
        (Venue mine, _) = await SeedTwoVenues("Cervezas", "Cervezas");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.True(await new CategoryRepository(asMine).NameExistsAsync("Cervezas", CancellationToken.None));
    }

    // The handler trims but does not lowercase: the name is shown as typed, so
    // it is the database that has to ignore case here.
    [Fact]
    public async Task NameExistsAsync_WhenTypedWithOtherCasing_IsStillTrue()
    {
        (Venue mine, _) = await SeedTwoVenues("Cervezas", "Tragos");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.True(await new CategoryRepository(asMine).NameExistsAsync("CERVEZAS", CancellationToken.None));
    }

    [Fact]
    public async Task NameExistsAsync_WhenOnlyAnotherVenueHasIt_IsFalse()
    {
        (Venue mine, _) = await SeedTwoVenues("Tragos", "Cervezas");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.False(await new CategoryRepository(asMine).NameExistsAsync("Cervezas", CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenAnotherVenueAlreadyHasThatName_StillCreatesTheCategory()
    {
        (Venue mine, _) = await SeedTwoVenues("Tragos", "Cervezas");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        CategoryRepository repository = new(asMine);

        await repository.AddAsync(Category.Create(mine.Id, "Cervezas", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(await repository.NameExistsAsync("Cervezas", CancellationToken.None));
    }

    // The index is the last line of defence: two administrators saving the same
    // new category at once both pass the check.
    [Fact]
    public async Task AddAsync_WhenThisVenueAlreadyHasThatName_IsRefusedByTheDatabase()
    {
        (Venue mine, _) = await SeedTwoVenues("Cervezas", "Tragos");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        await Assert.ThrowsAsync<DbUpdateException>(() => new CategoryRepository(asMine).AddAsync(
            Category.Create(mine.Id, "Cervezas", DateTimeOffset.UtcNow),
            CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_WhenThisVenueHasIt_IsTrue()
    {
        (Venue mine, _) = await SeedTwoVenues("Cervezas", "Tragos");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Category own = await asMine.Categories.SingleAsync();

        Assert.True(await new CategoryRepository(asMine).ExistsAsync(own.Id, CancellationToken.None));
    }

    // Another venue's id is not found, not forbidden: a product cannot be filed
    // under a category the venue next door made.
    [Fact]
    public async Task ExistsAsync_WhenItBelongsToAnotherVenue_IsFalse()
    {
        (Venue mine, Venue theirs) = await SeedTwoVenues("Tragos", "Cervezas");

        await using DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id);
        Category foreign = await asTheirs.Categories.SingleAsync();

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.False(await new CategoryRepository(asMine).ExistsAsync(foreign.Id, CancellationToken.None));
    }

    private async Task<(Venue Mine, Venue Theirs)> SeedTwoVenues(string mineCategory, string theirsCategory)
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        SeedCategory.For(seed, mine.Id, mineCategory);
        SeedCategory.For(seed, theirs.Id, theirsCategory);
        await seed.SaveChangesAsync();

        return (mine, theirs);
    }
}
