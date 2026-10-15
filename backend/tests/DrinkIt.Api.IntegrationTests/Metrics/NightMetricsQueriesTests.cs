using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Metrics;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Metrics;
using DrinkIt.Infrastructure.Persistence;

namespace DrinkIt.Api.IntegrationTests.Metrics;

/// <summary>US-47 to US-49: what a night's report is made of, read from the database.</summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightMetricsQueriesTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Saturday = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    private int _nextCode = 1000;

    [Fact]
    public async Task GetAsync_ForAnotherVenuesNight_IsNull()
    {
        Seeded seeded = await Seed();

        await using DrinkItDbContext theirs = sql.CreateContext(seeded.Theirs.Id);

        Assert.Null(await new NightMetricsQueries(theirs).GetAsync(seeded.Saturday.Id, CancellationToken.None));
    }

    // Vendido es pagado (US-48): cash nobody collected, a canceled order and
    // another night's orders are not this night's sales.
    [Fact]
    public async Task GetAsync_Always_CarriesOnlyThePaidOrdersOfThatNight()
    {
        Seeded seeded = await Seed();
        Order delivered = await APaidOrder(seeded, seeded.Saturday, [Gin(seeded, 2)], deliver: true);
        await AnOrderWaitingForCash(seeded, seeded.Saturday, cancel: false);
        await AnOrderWaitingForCash(seeded, seeded.Saturday, cancel: true);
        await APaidOrder(seeded, seeded.Friday, [Gin(seeded, 1)], deliver: true);

        NightSales sales = await SalesOf(seeded, seeded.Saturday);

        PaidOrder order = Assert.Single(sales.Orders);
        Assert.Equal(new PaidOrder(
            seeded.Pista.Id,
            PaymentMethod.Digital,
            9000m,
            OrderStatus.Delivered,
            delivered.PaidAt!.Value,
            delivered.ReadyAt,
            delivered.DeliveredAt), order);
    }

    // US-48, criterion 2 (US-42): what the table's balance gave travels with
    // the order, so the report can split it from what the phone paid.
    [Fact]
    public async Task GetAsync_ForATableOrder_CarriesWhatTheBalanceGave()
    {
        Seeded seeded = await Seed();
        Order order = Order.Place(seeded.Venue.Id, seeded.Saturday.Id, "María Quadro", NextCode(), [Gin(seeded, 2)]);
        order.PayFromBalance(seeded.Saturday.StartsAt.AddHours(1), 5000m);
        order.Enqueue(seeded.Pista.Id);
        await Save(seeded, order);

        NightSales sales = await SalesOf(seeded, seeded.Saturday);

        PaidOrder paid = Assert.Single(sales.Orders);
        Assert.Equal((PaymentMethod.VipBalance, 9000m, 5000m), (paid.Method, paid.Total, paid.BalanceAmount));
    }

    // US-49: by units, with the name and price each line was ordered at.
    [Fact]
    public async Task GetAsync_Always_SumsEachProductAcrossThePaidOrders()
    {
        Seeded seeded = await Seed();
        await APaidOrder(seeded, seeded.Saturday, [Gin(seeded, 2, 4500m), new NewOrderItem(seeded.Fernet.Id, "Fernet", 4000m, 1, null)]);
        await APaidOrder(seeded, seeded.Saturday, [Gin(seeded, 1, 5000m)]);

        NightSales sales = await SalesOf(seeded, seeded.Saturday);

        Assert.Equal(new ProductSales(seeded.Gin.Id, "Gin Tonic", 3, 14000m), sales.Products.Single(product => product.ProductId == seeded.Gin.Id));
        Assert.Equal(new ProductSales(seeded.Fernet.Id, "Fernet", 1, 4000m), sales.Products.Single(product => product.ProductId == seeded.Fernet.Id));
    }

    // US-36: the name the board and the customer read, the username when it
    // has none. In the order of their usernames, like the night's stock.
    [Fact]
    public async Task GetAsync_Always_NamesTheBarsAsTheirBoardsDo()
    {
        Seeded seeded = await Seed();

        NightSales sales = await SalesOf(seeded, seeded.Saturday);

        Assert.Equal([new NightBar(seeded.Patio.Id, "patio-bar"), new NightBar(seeded.Pista.Id, "Barra Pista")], sales.Bars);
    }

    private async Task<NightSales> SalesOf(Seeded seeded, Night night)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return (await new NightMetricsQueries(context).GetAsync(night.Id, CancellationToken.None))!;
    }

    private static NewOrderItem Gin(Seeded seeded, int units, decimal price = 4500m) => new(seeded.Gin.Id, "Gin Tonic", price, units, null);

    private async Task<Order> APaidOrder(Seeded seeded, Night night, NewOrderItem[] items, bool deliver = false)
    {
        Order order = Order.Place(seeded.Venue.Id, night.Id, "María Quadro", NextCode(), items);
        DateTimeOffset paidAt = night.StartsAt.AddHours(1);
        order.Pay(paidAt, PaymentMethod.Digital);
        order.Enqueue(seeded.Pista.Id);

        if (deliver)
        {
            order.StartPreparing(paidAt.AddMinutes(2));
            order.MarkReady(paidAt.AddMinutes(6));
            order.Deliver(paidAt.AddMinutes(9));
        }

        await Save(seeded, order);

        return order;
    }

    private async Task AnOrderWaitingForCash(Seeded seeded, Night night, bool cancel)
    {
        Order order = Order.Place(seeded.Venue.Id, night.Id, "María Quadro", NextCode(), [Gin(seeded, 1)]);
        order.AwaitPayment(PaymentMethod.Cash);

        if (cancel) order.Cancel();

        await Save(seeded, order);
    }

    private async Task Save(Seeded seeded, Order order)
    {
        await using DrinkItDbContext seed = sql.CreateContext(seeded.Venue.Id);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();
    }

    private OrderCode NextCode() => OrderCode.Parse($"K-{_nextCode++}");

    private sealed record Seeded(Venue Venue, Venue Theirs, StaffUser Pista, StaffUser Patio, Night Friday, Night Saturday, Product Gin, Product Fernet);

    private async Task<Seeded> Seed()
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(mine.Id);
        StaffUser pista = StaffUser.Create(mine.Id, "pista-bar", "hash", StaffRole.Kds, "Barra Pista");
        StaffUser patio = StaffUser.Create(mine.Id, "patio-bar", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(mine.Id, "till-1", "hash", StaffRole.Cashier);

        Seeded seeded = new(
            mine,
            theirs,
            pista,
            patio,
            Night.Create(mine.Id, "Friday", Saturday.AddDays(-1), Saturday.AddDays(-1).AddHours(7), [pista, till]),
            Night.Create(mine.Id, "Saturday", Saturday, Saturday.AddHours(7), [pista, patio, till]),
            Product.Create(mine.Id, "Gin Tonic", null, null, 4500m, 30, category.Id),
            Product.Create(mine.Id, "Fernet", null, null, 4000m, 30, category.Id));

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.Categories.Add(category);
        seed.StaffUsers.AddRange(pista, patio, till);
        seed.Nights.AddRange(seeded.Friday, seeded.Saturday);
        seed.Products.AddRange(seeded.Gin, seeded.Fernet);
        await seed.SaveChangesAsync();

        return seeded;
    }
}
