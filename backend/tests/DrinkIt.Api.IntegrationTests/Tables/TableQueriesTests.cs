using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Tables;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Tables;

namespace DrinkIt.Api.IntegrationTests.Tables;

[Collection(nameof(SqlServerCollection))]
public sealed class TableQueriesTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

    // By number and not by creation: the administrator looks for "Mesa 12", and
    // the one added last is not the one to find at the bottom.
    [Fact]
    public async Task ListAsync_WhenTheVenueHasSeveral_ReturnsThemByNumber()
    {
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.Add(mine);
        seed.Tables.AddRange(
            Table.Create(mine.Id, 12, Evening),
            Table.Create(mine.Id, 2, Evening.AddMinutes(1)),
            Table.Create(mine.Id, 7, Evening.AddMinutes(2)));
        await seed.SaveChangesAsync();

        IReadOnlyList<TableSummary> listed = await new TableQueries(seed).ListAsync(CancellationToken.None);

        Assert.Equal([2, 7, 12], listed.Select(table => table.Number));
    }

    // Put-away tables are listed, flagged: they are the ones to bring back.
    [Fact]
    public async Task ListAsync_WhenATableIsPutAway_ListsItInactive()
    {
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Table away = Table.Create(mine.Id, 3, Evening);
        away.Deactivate();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.Add(mine);
        seed.Tables.AddRange(Table.Create(mine.Id, 1, Evening), away);
        await seed.SaveChangesAsync();

        IReadOnlyList<TableSummary> listed = await new TableQueries(seed).ListAsync(CancellationToken.None);

        Assert.Equal([true, false], listed.Select(table => table.IsActive));
    }

    // The invariant that matters most in the data model: a venue never sees
    // another venue's rows.
    [Fact]
    public async Task ListAsync_WhenAnotherVenueHasTables_LeavesThemOut()
    {
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.Tables.Add(Table.Create(mine.Id, 1, Evening));
        seed.Tables.Add(Table.Create(theirs.Id, 9, Evening));
        await seed.SaveChangesAsync();

        IReadOnlyList<TableSummary> listed = await new TableQueries(seed).ListAsync(CancellationToken.None);

        Assert.Equal([1], listed.Select(table => table.Number));
    }
}
