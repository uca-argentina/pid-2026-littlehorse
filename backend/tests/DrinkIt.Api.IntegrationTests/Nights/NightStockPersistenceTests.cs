using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-37: what a night has of each product is stored per night and per
/// product, and a venue never reads another's.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightStockPersistenceTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public async Task SaveChangesAsync_WhenTheStockIsOpened_KeepsWhatWasLoadedAndWhatIsLeft()
    {
        (Venue venue, Night night, Product product) = await AVenueWithANightAndAProduct();

        await using (DrinkItDbContext write = sql.CreateContext(venue.Id))
        {
            write.NightStocks.Add(NightStock.Open(venue.Id, night.Id, product.Id, 12));
            await write.SaveChangesAsync();
        }

        await using DrinkItDbContext read = sql.CreateContext(venue.Id);
        NightStock stored = await read.NightStocks.SingleAsync(stock => stock.NightId == night.Id);

        Assert.Equal(product.Id, stored.ProductId);
        Assert.Equal(12, stored.Loaded);
        Assert.Equal(12, stored.Remaining);
        Assert.Equal(0, stored.Sold);
    }

    [Fact]
    public async Task SaveChangesAsync_AfterAnAdjustment_KeepsTheNewTotals()
    {
        (Venue venue, Night night, Product product) = await AVenueWithANightAndAProduct();
        NightStock stock = NightStock.Open(venue.Id, night.Id, product.Id, 12);

        await using (DrinkItDbContext seed = sql.CreateContext(venue.Id))
        {
            seed.NightStocks.Add(stock);
            await seed.SaveChangesAsync();
        }

        await using (DrinkItDbContext edit = sql.CreateContext(venue.Id))
        {
            NightStock tracked = await edit.NightStocks.SingleAsync(row => row.Id == stock.Id);
            tracked.Adjust(8);
            await edit.SaveChangesAsync();
        }

        await using DrinkItDbContext read = sql.CreateContext(venue.Id);
        NightStock stored = await read.NightStocks.SingleAsync(row => row.Id == stock.Id);

        Assert.Equal(20, stored.Loaded);
        Assert.Equal(20, stored.Remaining);
    }

    // The one invariant of the model that leaks real data when it breaks.
    [Fact]
    public async Task NightStocks_WhenReadFromAnotherVenue_AreNotVisible()
    {
        (Venue mine, Night myNight, Product myProduct) = await AVenueWithANightAndAProduct();
        (Venue theirs, Night theirNight, Product theirProduct) = await AVenueWithANightAndAProduct();

        await using (DrinkItDbContext seedMine = sql.CreateContext(mine.Id))
        {
            seedMine.NightStocks.Add(NightStock.Open(mine.Id, myNight.Id, myProduct.Id, 5));
            await seedMine.SaveChangesAsync();
        }

        await using (DrinkItDbContext seedTheirs = sql.CreateContext(theirs.Id))
        {
            seedTheirs.NightStocks.Add(NightStock.Open(theirs.Id, theirNight.Id, theirProduct.Id, 7));
            await seedTheirs.SaveChangesAsync();
        }

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        int[] visible = await asMine.NightStocks
            .Where(stock => stock.NightId == myNight.Id || stock.NightId == theirNight.Id)
            .Select(stock => stock.Loaded)
            .ToArrayAsync();

        Assert.Equal([5], visible);
    }

    // One row per product per night: a second one would make "how many are
    // left" an argument between two rows.
    [Fact]
    public async Task SaveChangesAsync_WhenTheNightAlreadyHasThatProduct_IsRefused()
    {
        (Venue venue, Night night, Product product) = await AVenueWithANightAndAProduct();

        await using (DrinkItDbContext first = sql.CreateContext(venue.Id))
        {
            first.NightStocks.Add(NightStock.Open(venue.Id, night.Id, product.Id, 5));
            await first.SaveChangesAsync();
        }

        await using DrinkItDbContext second = sql.CreateContext(venue.Id);
        second.NightStocks.Add(NightStock.Open(venue.Id, night.Id, product.Id, 9));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    private async Task<(Venue Venue, Night Night, Product Product)> AVenueWithANightAndAProduct()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        StaffUser kds = StaffUser.Create(venue.Id, $"kds-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier);
        Night night = Night.Create(venue.Id, "Saturday", Opening, Opening.AddHours(7), [kds, till]);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        Category category = SeedCategory.For(seed, venue.Id);
        Product product = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);
        seed.StaffUsers.AddRange(kds, till);
        seed.Nights.Add(night);
        seed.Products.Add(product);
        await seed.SaveChangesAsync();

        return (venue, night, product);
    }
}
