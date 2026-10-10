using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-37, migration: before nights owned the stock, <c>Product.Stock</c> was
/// the only number, already net of every sale. The backfill gives each existing
/// night its own row so that the chain of nights ends exactly where that number
/// is, and what each night sold is what its orders say.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightStockBackfillTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Saturday = Friday.AddDays(1);

    [Fact]
    public async Task Backfill_WhenTheOnlyNightSoldSomeUnits_LoadsWhatWasLeftPlusWhatWasSold()
    {
        Fixture f = await AVenueWithTwoNights();
        await f.PlaceAnOrder(f.Friday, quantity: 5);

        await Backfill();

        NightStock friday = await f.StockOf(f.Friday);

        Assert.Equal(17, friday.Loaded);
        Assert.Equal(12, friday.Remaining);
        Assert.Equal(5, friday.Sold);
    }

    // Saturday starts with what Friday had left: the chain is what makes the
    // last night's remaining equal the product's number from before.
    [Fact]
    public async Task Backfill_WhenSeveralNightsSold_ChainsEachOneFromWhatTheLastLeft()
    {
        Fixture f = await AVenueWithTwoNights();
        await f.PlaceAnOrder(f.Friday, quantity: 5);
        await f.PlaceAnOrder(f.Saturday, quantity: 3);

        await Backfill();

        NightStock friday = await f.StockOf(f.Friday);
        NightStock saturday = await f.StockOf(f.Saturday);

        Assert.Equal((20, 15), (friday.Loaded, friday.Remaining));
        Assert.Equal((15, 12), (saturday.Loaded, saturday.Remaining));
    }

    [Fact]
    public async Task Backfill_WhenAnOrderWasCanceled_DoesNotCountItAsSold()
    {
        Fixture f = await AVenueWithTwoNights();
        Order canceled = await f.PlaceAnOrder(f.Friday, quantity: 4);

        await using (DrinkItDbContext moving = sql.CreateContext(f.Venue.Id))
        {
            await moving.Orders
                .Where(row => row.Id == canceled.Id)
                .ExecuteUpdateAsync(row => row.SetProperty(order => order.Status, OrderStatus.Canceled));
        }

        await Backfill();

        NightStock friday = await f.StockOf(f.Friday);

        Assert.Equal(0, friday.Sold);
        Assert.Equal(12, friday.Remaining);
    }

    [Fact]
    public async Task Backfill_WhenRunTwice_KeepsOneRowPerNightAndProduct()
    {
        Fixture f = await AVenueWithTwoNights();

        await Backfill();
        await Backfill();

        await using DrinkItDbContext read = sql.CreateContext(f.Venue.Id);
        int rows = await read.NightStocks.CountAsync(stock => stock.ProductId == f.Product.Id);

        Assert.Equal(2, rows);
    }

    private async Task Backfill()
    {
        await using DrinkItDbContext context = sql.CreateContext(Guid.Empty);
        await context.Database.ExecuteSqlRawAsync(NightStockBackfill.Sql);
    }

    private async Task<Fixture> AVenueWithTwoNights()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        StaffUser kds = StaffUser.Create(venue.Id, $"kds-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier);
        Night friday = Night.Create(venue.Id, "Friday", Friday, Friday.AddHours(7), [kds, till]);
        Night saturday = Night.Create(venue.Id, "Saturday", Saturday, Saturday.AddHours(7), [kds, till]);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        Category category = SeedCategory.For(seed, venue.Id);
        // Twelve is the number from before nights: already net of every sale.
        Product product = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 12, category.Id);
        seed.StaffUsers.AddRange(kds, till);
        seed.Nights.AddRange(friday, saturday);
        seed.Products.Add(product);
        await seed.SaveChangesAsync();

        return new Fixture(sql, venue, friday, saturday, product);
    }

    private sealed class Fixture(SqlServerFixture sql, Venue venue, Night friday, Night saturday, Product product)
    {
        private int _next = 1;

        public Venue Venue => venue;

        public Night Friday => friday;

        public Night Saturday => saturday;

        public Product Product => product;

        public async Task<Order> PlaceAnOrder(Night night, int quantity)
        {
            Order order = Order.Place(
                venue.Id,
                night.Id,
                "María Quadro",
                OrderCode.Parse($"K-{4800 + _next++}"),
                [new NewOrderItem(product.Id, product.Name, product.Price, quantity, null)]);

            order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);
            order.Enqueue();

            await using DrinkItDbContext write = sql.CreateContext(venue.Id);
            write.Orders.Add(order);
            write.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
            await write.SaveChangesAsync();

            return order;
        }

        public async Task<NightStock> StockOf(Night night)
        {
            await using DrinkItDbContext read = sql.CreateContext(venue.Id);

            return await read.NightStocks.SingleAsync(stock => stock.NightId == night.Id && stock.ProductId == product.Id);
        }
    }
}
