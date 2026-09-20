using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;

namespace DrinkIt.Api.IntegrationTests.Menu;

/// <summary>
/// US-09: what a customer sees after scanning the QR. A different read from the
/// administration listing and not a filter over it — this one answers somebody
/// with no session, so what it leaves out matters as much as what it brings.
/// </summary>
/// <remarks>
/// The nightly switch is flipped through the domain, with US-07's
/// Product.MarkUnavailable. The soft delete still goes straight through EF:
/// US-08 has not built the method for it, and what is being tested is the
/// WHERE this query runs, which the row state has to survive however it got
/// written.
/// </remarks>
[Collection(nameof(SqlServerCollection))]
public sealed class MenuQueriesTests(SqlServerFixture sql)
{
    [Fact]
    public async Task ListForMenuAsync_WhenTheVenueSellsSomething_BringsWhatTheCardShows()
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Products.Add(Product.Create(mine.Id, "Gin Tonic", "Gin, tónica, lima", null, 4500m, 20));
        await seed.SaveChangesAsync();

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(CancellationToken.None))
            .Single(product => product.Name == "Gin Tonic");

        Assert.Equal("Gin, tónica, lima", item.Description);
        Assert.Equal(4500m, item.Price);
        Assert.True(item.IsOrderable);
    }

    // US-08 will let an administrator take a product off the menu. Soft-deleted
    // rows stay in the table for the old orders that point at them, and the
    // customer must never be offered one.
    [Fact]
    public async Task ListForMenuAsync_WhenAProductWasTakenOffTheMenu_LeavesItOut()
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product gone = Product.Create(mine.Id, "Daiquiri", null, null, 4000m, 5);
        seed.Products.AddRange(gone, Product.Create(mine.Id, "Negroni", null, null, 5000m, 5));
        TakeOffTheMenu(seed, gone);
        await seed.SaveChangesAsync();

        IReadOnlyList<MenuItem> menu = await new ProductQueries(seed).ListForMenuAsync(CancellationToken.None);

        Assert.Equal(["Negroni"], menu.Select(item => item.Name));
    }

    /// <summary>
    /// Shown and not hidden, on purpose: a product that disappeared would read
    /// as a mistake and send the customer to ask at the bar, which is the walk
    /// this whole product exists to avoid. Both reasons look the same from the
    /// phone — the card says it cannot be ordered and never why.
    /// </summary>
    [Theory]
    [InlineData(0, true, false)]
    [InlineData(10, false, false)]
    [InlineData(10, true, true)]
    public async Task ListForMenuAsync_WhenAProductCannotBeServed_StillShowsItAsNotOrderable(
        int stock,
        bool available,
        bool expected)
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product product = Product.Create(mine.Id, "Aperol Spritz", null, null, 6000m, stock);
        seed.Products.Add(product);

        if (!available) product.MarkUnavailable();

        await seed.SaveChangesAsync();

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(CancellationToken.None))
            .Single(x => x.Name == "Aperol Spritz");

        Assert.Equal(expected, item.IsOrderable);
    }

    // The most important invariant of the data model, on the one endpoint that
    // answers without a token: the slug in the URL is all a stranger controls.
    [Fact]
    public async Task ListForMenuAsync_WhenAnotherVenueSellsIt_LeavesItOut()
    {
        Venue mine = await SeedVenue();
        Venue theirs = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Products.AddRange(
            Product.Create(mine.Id, "Lo mio", null, null, 1000m, 5),
            Product.Create(theirs.Id, "Lo de ellos", null, null, 1000m, 5));
        await seed.SaveChangesAsync();

        IReadOnlyList<MenuItem> menu = await new ProductQueries(seed).ListForMenuAsync(CancellationToken.None);

        Assert.Equal(["Lo mio"], menu.Select(item => item.Name));
    }

    // Nothing about how many are left reaches the phone. The card says whether
    // it can be ordered and no more: a stock count is the venue's business.
    [Fact]
    public void MenuItem_WhenInspected_CarriesNoStockCount()
    {
        Assert.DoesNotContain(
            typeof(MenuItem).GetProperties(),
            property => property.Name.Contains("Stock", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The soft delete, written straight to the row: US-08 has not built the
    /// domain method for it. The nightly switch no longer comes through here.
    /// </summary>
    private static void TakeOffTheMenu(DrinkItDbContext context, Product product) =>
        context.Entry(product).Property(item => item.IsActive).CurrentValue = false;

    private async Task<Venue> SeedVenue()
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue venue = Venue.Create("Bar Alfa", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        await seed.SaveChangesAsync();

        return venue;
    }
}
