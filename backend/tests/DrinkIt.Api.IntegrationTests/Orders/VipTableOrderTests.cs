using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using DrinkIt.Domain.VipAccounts;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Tables;
using DrinkIt.Infrastructure.VipAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// US-41 against a real database: an order paid from a VIP table's balance is
/// written with the balance it spent, and two phones of the same table cannot
/// spend the same money twice.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class VipTableOrderTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Tonight = new(2026, 9, 17, 2, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Confirm_WhenTheTableHasBalance_PaysFromItAndCarriesItToTheTable()
    {
        Seeded seeded = await AVenueWithATable(budget: 20_000m);

        Result<ConfirmedOrder> result = await Confirm(seeded, "phone-1");

        await using DrinkItDbContext check = sql.CreateContext(seeded.Venue.Id);
        Order stored = await check.Orders.SingleAsync();
        VipAccount account = await check.VipAccounts.SingleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Queued, stored.Status);
        Assert.Equal(PaymentMethod.VipBalance, stored.Method);
        Assert.Equal(5, stored.TableNumber);
        Assert.Equal(9_000m, account.Spent);
    }

    // US-41, criterion 3: the balance covers one of the two orders. One is
    // paid; the other sees it does not cover it, and nothing is taken twice.
    [Fact]
    public async Task Confirm_WhenTwoPhonesOfTheTableConfirmAtOnce_OnlyOneIsPaid()
    {
        Seeded seeded = await AVenueWithATable(budget: 10_000m);

        Result<ConfirmedOrder>[] results = await Task.WhenAll(Confirm(seeded, "phone-1"), Confirm(seeded, "phone-2"));

        await using DrinkItDbContext check = sql.CreateContext(seeded.Venue.Id);

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.Error == VipAccountErrors.BalanceShort);
        Assert.Single(await check.Orders.ToListAsync());
        Assert.Equal(9_000m, (await check.VipAccounts.SingleAsync()).Spent);
    }

    // The code of a table next door finds nothing here, the same as one invented.
    [Fact]
    public async Task Confirm_WhenTheCodeIsAnotherVenues_FailsNotFoundAndStoresNothing()
    {
        Seeded mine = await AVenueWithATable(budget: 20_000m);
        Seeded theirs = await AVenueWithATable(budget: 20_000m);

        Result<ConfirmedOrder> result = await Confirm(mine with { AccessCode = theirs.AccessCode }, "phone-1");

        await using DrinkItDbContext check = sql.CreateContext(mine.Venue.Id);

        Assert.Equal(VipAccountErrors.NotFound, result.Error);
        Assert.Empty(await check.Orders.ToListAsync());
    }

    private sealed record Seeded(Venue Venue, Product Gin, string AccessCode);

    /// <summary>Two gin tonics at 4500 from the table: 9000.</summary>
    private async Task<Result<ConfirmedOrder>> Confirm(Seeded seeded, string key)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        FixedVenue current = new(seeded.Venue.Id);
        FixedClock clock = new(Tonight);

        ConfirmOrderHandler handler = new(
            new OrderRepository(context, new NoEvents(), NullLogger<OrderRepository>.Instance),
            new ProductsForOrdering(context),
            new BarsForOrdering(context),
            new OrderCodeSequence(context, current),
            [new VipBalancePaymentStrategy(new VipAccountRepository(context), new TableRepository(context), clock)],
            current,
            new UnderwayNightLookup(context),
            clock);

        return await handler.HandleAsync(
            new ConfirmOrderCommand("María Quadro", PaymentMethod.VipBalance, key, [new OrderLineRequest(seeded.Gin.Id, 2, null)], seeded.AccessCode),
            CancellationToken.None);
    }

    private async Task<Seeded> AVenueWithATable(decimal budget)
    {
        Venue venue = Venue.Create("Test Bar", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 100, category.Id);
        StaffUser bar = StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds);
        StaffUser[] crew = [bar, StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier), StaffUser.Create(venue.Id, "martin", "hash", StaffRole.Waiter)];
        Night tonight = Night.Create(venue.Id, "Tonight", Tonight.AddHours(-3), Tonight.AddHours(4), crew);
        Table five = Table.Create(venue.Id, 5, 8, null, Tonight.AddDays(-1));
        VipAccount account = VipAccount.Open(tonight, five, budget, Tonight.AddHours(-4));

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        seed.StaffUsers.AddRange(crew);
        seed.Nights.Add(tonight);
        seed.Tables.Add(five);
        seed.VipAccounts.Add(account);
        seed.BarStocks.Add(new BarStock { VenueId = venue.Id, NightId = tonight.Id, KdsId = bar.Id, ProductId = gin.Id, Units = 100 });
        await seed.SaveChangesAsync();

        return new Seeded(venue, gin, account.AccessCode);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id { get; } = id;
    }

    private sealed class NoEvents : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
