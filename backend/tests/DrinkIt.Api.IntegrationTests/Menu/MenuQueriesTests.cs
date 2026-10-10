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
        seed.Products.Add(Product.Create(mine.Id, "Gin Tonic", "Gin, tónica, lima", null, 4500m, 20, drinks));
        await seed.SaveChangesAsync();

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(night: null, CancellationToken.None))
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

        IReadOnlyList<MenuItem> menu = await new ProductQueries(seed).ListForMenuAsync(night: null, CancellationToken.None);

        Assert.Equal(["Negroni"], menu.Select(item => item.Name));
    }

    /// <summary>
    /// Shown and not hidden, on purpose: a product that disappeared would read
    /// as a mistake and send the customer to ask at the bar, which is the walk
    /// this whole product exists to avoid. Both reasons look the same from the
    /// phone — the card says it cannot be ordered and never why.
    /// </summary>
    [Theory]
    [InlineData(10, false, false)]
    [InlineData(10, true, true)]
    public async Task ListForMenuAsync_WhenTheSwitchIsOff_StillShowsItAsNotOrderable(
        int stock,
        bool available,
        bool expected)
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product product = Product.Create(mine.Id, "Aperol Spritz", null, null, 6000m, stock, await CategoryOf(seed));
        seed.Products.Add(product);

        if (!available) product.MarkUnavailable();

        await seed.SaveChangesAsync();

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(night: null, CancellationToken.None))
            .Single(x => x.Name == "Aperol Spritz");

        Assert.Equal(expected, item.IsOrderable);
    }

    // US-37, decision of 2026-10-10: with no night on there is nothing to be
    // sold out of. The menu reads in full and the phone says the venue is not
    // taking orders; marking a drink sold out would be a lie about a stock
    // that does not exist yet.
    [Fact]
    public async Task ListForMenuAsync_WhenNoNightIsOn_DoesNotMarkAnythingSoldOut()
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Products.Add(Product.Create(mine.Id, "Aperol Spritz", null, null, 6000m, 0, await CategoryOf(seed)));
        await seed.SaveChangesAsync();

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(night: null, CancellationToken.None))
            .Single(x => x.Name == "Aperol Spritz");

        Assert.True(item.IsOrderable);
    }

    // The number on the product is only where the first night starts from.
    // What is on the shelf tonight is the night's.
    [Theory]
    [InlineData(20, 0, false)]
    [InlineData(0, 5, true)]
    public async Task ListForMenuAsync_WhenANightIsOn_UsesWhatTheNightHasLeft(
        int productStock,
        int nightRemaining,
        bool expected)
    {
        Venue mine = await SeedVenue();
        (Product product, Guid night) = await SeedProductWithANight(mine, productStock, nightRemaining);

        await using DrinkItDbContext read = sql.CreateContext(mine.Id);

        MenuItem item = (await new ProductQueries(read).ListForMenuAsync(night, CancellationToken.None))
            .Single(x => x.Id == product.Id);

        Assert.Equal(expected, item.IsOrderable);
    }

    // A product the night never opened a row for is not on its shelf.
    [Fact]
    public async Task ListForMenuAsync_WhenTheNightHasNoRowForAProduct_ShowsItAsNotOrderable()
    {
        Venue mine = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        Product product = Product.Create(mine.Id, "Aperol Spritz", null, null, 6000m, 10, await CategoryOf(seed));
        seed.Products.Add(product);
        Night night = ANight(mine);
        seed.Nights.Add(night);
        await seed.SaveChangesAsync();

        MenuItem item = (await new ProductQueries(seed).ListForMenuAsync(night.Id, CancellationToken.None))
            .Single(x => x.Id == product.Id);

        Assert.False(item.IsOrderable);
    }

    // Two nights: what the other one has left says nothing about this one.
    [Fact]
    public async Task ListForMenuAsync_WhenAnotherNightHasStock_IgnoresIt()
    {
        Venue mine = await SeedVenue();
        (Product product, Guid tonight) = await SeedProductWithANight(mine, productStock: 10, nightRemaining: 0);
        await SeedAnotherNightWithStock(mine, product, remaining: 8);

        await using DrinkItDbContext read = sql.CreateContext(mine.Id);

        MenuItem item = (await new ProductQueries(read).ListForMenuAsync(tonight, CancellationToken.None))
            .Single(x => x.Id == product.Id);

        Assert.False(item.IsOrderable);
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

        IReadOnlyList<MenuItem> menu = await new ProductQueries(seed).ListForMenuAsync(night: null, CancellationToken.None);

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

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    private static Night ANight(Venue venue, int dayOffset = 0) =>
        Night.Create(
            venue.Id,
            $"Night {dayOffset}",
            Opening.AddDays(dayOffset),
            Opening.AddDays(dayOffset).AddHours(7),
            [
                StaffUser.Create(venue.Id, $"kds-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds),
                StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier),
            ]);

    private async Task<(Product Product, Guid NightId)> SeedProductWithANight(Venue venue, int productStock, int nightRemaining)
    {
        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        Product product = Product.Create(venue.Id, "Aperol Spritz", null, null, 6000m, productStock, await CategoryOf(seed));
        StaffUser kds = StaffUser.Create(venue.Id, $"kds-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier);
        Night night = Night.Create(venue.Id, "Tonight", Opening, Opening.AddHours(7), [kds, till]);
        NightStock stock = NightStock.Open(venue.Id, night.Id, product.Id, nightRemaining);

        seed.Products.Add(product);
        seed.StaffUsers.AddRange(kds, till);
        seed.Nights.Add(night);
        seed.NightStocks.Add(stock);
        await seed.SaveChangesAsync();

        return (product, night.Id);
    }

    private async Task SeedAnotherNightWithStock(Venue venue, Product product, int remaining)
    {
        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        StaffUser kds = StaffUser.Create(venue.Id, $"kds-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier);
        Night other = Night.Create(venue.Id, "Next", Opening.AddDays(1), Opening.AddDays(1).AddHours(7), [kds, till]);

        seed.StaffUsers.AddRange(kds, till);
        seed.Nights.Add(other);
        seed.NightStocks.Add(NightStock.Open(venue.Id, other.Id, product.Id, remaining));
        await seed.SaveChangesAsync();
    }

    /// <summary>
    /// The soft delete, written straight to the row: US-08 has not built the
    /// domain method for it. The nightly switch no longer comes through here.
    /// </summary>
    private static void TakeOffTheMenu(DrinkItDbContext context, Product product) =>
        context.Entry(product).Property(item => item.IsActive).CurrentValue = false;

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
