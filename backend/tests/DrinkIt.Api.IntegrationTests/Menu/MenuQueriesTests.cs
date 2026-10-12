using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        Guid drinks = await CategoryOf(seed);
        Product gin = Product.Create(mine.Id, "Gin Tonic", "Gin, tónica, lima", null, 4500m, 20, drinks);
        seed.Products.Add(gin);
        await seed.SaveChangesAsync();
        Guid tonight = await ANightWith(mine, (gin, 20));

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(tonight, CancellationToken.None))
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
        Guid drinks = await CategoryOf(seed);
        Product gone = Product.Create(mine.Id, "Daiquiri", null, null, 4000m, 5, drinks);
        seed.Products.AddRange(gone, Product.Create(mine.Id, "Negroni", null, null, 5000m, 5, drinks));
        TakeOffTheMenu(seed, gone);
        await seed.SaveChangesAsync();

        IReadOnlyList<MenuItem> menu = await new ProductQueries(seed).ListForMenuAsync(null, CancellationToken.None);

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
        int inTonightsBar,
        bool available,
        bool expected)
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product product = Product.Create(mine.Id, "Aperol Spritz", null, null, 6000m, 10, await CategoryOf(seed));
        seed.Products.Add(product);

        if (!available) product.MarkUnavailable();

        await seed.SaveChangesAsync();
        Guid tonight = await ANightWith(mine, (product, inTonightsBar));

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(tonight, CancellationToken.None))
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
        await using DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id);
        seed.Products.AddRange(
            Product.Create(mine.Id, "Lo mio", null, null, 1000m, 5, await CategoryOf(seed)),
            Product.Create(theirs.Id, "Lo de ellos", null, null, 1000m, 5, await CategoryOf(asTheirs)));
        await seed.SaveChangesAsync();

        IReadOnlyList<MenuItem> menu = await new ProductQueries(seed).ListForMenuAsync(null, CancellationToken.None);

        Assert.Equal(["Lo mio"], menu.Select(item => item.Name));
    }

    // US-37: the venue has 20, but tonight's bars were not loaded with any.
    // What a customer can order is what a KDS can serve tonight.
    [Fact]
    public async Task ListForMenuAsync_WhenTheVenueHasItButNoBarOfTonightDoes_ShowsItAsNotOrderable()
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product gin = Product.Create(mine.Id, "Gin Tonic", null, null, 4500m, 20, await CategoryOf(seed));
        seed.Products.Add(gin);
        await seed.SaveChangesAsync();
        Guid tonight = await ANightWith(mine);

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(tonight, CancellationToken.None)).Single();

        Assert.False(item.IsOrderable);
    }

    // Two KDS, only one of them loaded: one is enough, the order goes there.
    [Fact]
    public async Task ListForMenuAsync_WhenOneBarOfTonightHasIt_ShowsItAsOrderable()
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product gin = Product.Create(mine.Id, "Gin Tonic", null, null, 4500m, 20, await CategoryOf(seed));
        seed.Products.Add(gin);
        await seed.SaveChangesAsync();
        Guid tonight = await ANightWith(mine, (gin, 0), (gin, 3));

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(tonight, CancellationToken.None)).Single();

        Assert.True(item.IsOrderable);
    }

    // Another night's bar is that night's: it serves nothing tonight.
    [Fact]
    public async Task ListForMenuAsync_WhenOnlyAnotherNightsBarHasIt_ShowsItAsNotOrderable()
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product gin = Product.Create(mine.Id, "Gin Tonic", null, null, 4500m, 20, await CategoryOf(seed));
        seed.Products.Add(gin);
        await seed.SaveChangesAsync();
        await ANightWith(mine, (gin, 5));
        Guid tonight = await ANightWith(mine, startingDaysLater: 1);

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(tonight, CancellationToken.None)).Single();

        Assert.False(item.IsOrderable);
    }

    // No night on: still readable (US-35), and nothing on it can be ordered.
    [Fact]
    public async Task ListForMenuAsync_WhenNoNightIsOn_ShowsEverythingAsNotOrderable()
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product gin = Product.Create(mine.Id, "Gin Tonic", null, null, 4500m, 20, await CategoryOf(seed));
        seed.Products.Add(gin);
        await seed.SaveChangesAsync();
        await ANightWith(mine, (gin, 5));

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(null, CancellationToken.None)).Single();

        Assert.False(item.IsOrderable);
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

    /// <summary>
    /// A night of the venue, worked by one KDS per load given, each holding
    /// those units for the night. Only what the query reads: the general stock
    /// is the product's, set when it was seeded.
    /// </summary>
    private async Task<Guid> ANightWith(Venue venue, params (Product Product, int Units)[] loads) =>
        await ANightWith(venue, startingDaysLater: 0, loads);

    private async Task<Guid> ANightWith(Venue venue, int startingDaysLater, params (Product Product, int Units)[] loads)
    {
        DateTimeOffset startsAt = new DateTimeOffset(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3)).AddDays(startingDaysLater);
        string suffix = Guid.NewGuid().ToString("N")[..8];
        StaffUser[] bars = [.. loads.Select((_, index) => StaffUser.Create(venue.Id, $"bar-{index}-{suffix}", "hash", StaffRole.Kds))];
        StaffUser kds = bars.FirstOrDefault() ?? StaffUser.Create(venue.Id, $"bar-{suffix}", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{suffix}", "hash", StaffRole.Cashier);
        Night night = Night.Create(venue.Id, "Saturday", startsAt, startsAt.AddHours(7), [kds, .. bars, till]);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.StaffUsers.AddRange([.. new[] { kds }.Union(bars), till]);
        seed.Nights.Add(night);
        seed.BarStocks.AddRange(loads.Select((load, index) => new BarStock
        {
            VenueId = venue.Id,
            NightId = night.Id,
            KdsId = bars[index].Id,
            ProductId = load.Product.Id,
            Units = load.Units,
        }));
        await seed.SaveChangesAsync();

        return night.Id;
    }

    /// <summary>The one category the venue was seeded with: the context only sees its own.</summary>
    private static async Task<Guid> CategoryOf(DrinkItDbContext context) =>
        (await context.Categories.SingleAsync()).Id;

    private async Task<Venue> SeedVenue()
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue venue = Venue.Create("Bar Alfa", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        SeedCategory.For(seed, venue.Id);
        await seed.SaveChangesAsync();

        return venue;
    }
}
