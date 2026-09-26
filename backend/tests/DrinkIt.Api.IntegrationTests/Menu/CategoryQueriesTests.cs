using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;

namespace DrinkIt.Api.IntegrationTests.Menu;

[Collection(nameof(SqlServerCollection))]
public sealed class CategoryQueriesTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    // The first one created is the first tab drawn: not alphabetical, and not
    // whatever order the database happens to return.
    [Fact]
    public async Task ListAsync_WhenTheVenueHasSeveral_ReturnsThemInTheOrderTheyWereCreated()
    {
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.Add(mine);
        seed.Categories.AddRange(
            Category.Create(mine.Id, "Sin alcohol", Morning.AddMinutes(2)),
            Category.Create(mine.Id, "Tragos", Morning),
            Category.Create(mine.Id, "Cervezas", Morning.AddMinutes(1)));
        await seed.SaveChangesAsync();

        IReadOnlyList<CategoryListItem> listed = await new CategoryQueries(seed).ListAsync(CancellationToken.None);

        Assert.Equal(["Tragos", "Cervezas", "Sin alcohol"], listed.Select(category => category.Name));
    }

    [Fact]
    public async Task ListAsync_WhenAnotherVenueHasCategories_LeavesThemOut()
    {
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        SeedCategory.For(seed, mine.Id, "Tragos");
        SeedCategory.For(seed, theirs.Id, "Vinos");
        await seed.SaveChangesAsync();

        IReadOnlyList<CategoryListItem> listed = await new CategoryQueries(seed).ListAsync(CancellationToken.None);

        Assert.Equal(["Tragos"], listed.Select(category => category.Name));
    }
}
