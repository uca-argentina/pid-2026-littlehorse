using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Kds;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Promotions;
using DrinkIt.Infrastructure.StampCards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// US-51 to US-53 against a real database: a combo holds no stock, and what it
/// sells comes off its components at the bar the order goes to, in the same
/// transaction as the order.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class ComboOrderTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Tonight = new(2026, 9, 17, 2, 30, 0, TimeSpan.Zero);

    // US-51, criterion 1: 2 combos of 2 Fernet and 1 ice.
    [Fact]
    public async Task Confirm_WhenAComboIsPaid_TakesEachComponentOffTheBarAndTheGeneralStock()
    {
        Seeded seeded = await Seed();

        Result<ConfirmedOrder> result = await Confirm(seeded, [new OrderLineRequest(seeded.Combo.Id, 2, null)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(18000m, result.Value.Total);
        Assert.Equal(6, await UnitsAt(seeded, seeded.MainBar, seeded.Fernet));
        Assert.Equal(8, await UnitsAt(seeded, seeded.MainBar, seeded.Ice));
        Assert.Equal(6, await GeneralStock(seeded, seeded.Fernet));
        Assert.Equal(8, await GeneralStock(seeded, seeded.Ice));
    }

    // A combo and a loose drink of the same product are asked for together.
    [Fact]
    public async Task Confirm_WhenAComboAndALooseDrinkShareAProductThatDoesNotCoverBoth_RefusesNamingWhatWasOrdered()
    {
        Seeded seeded = await Seed(fernet: 5);

        Result<ConfirmedOrder> result = await Confirm(
            seeded,
            [new OrderLineRequest(seeded.Combo.Id, 2, null), new OrderLineRequest(seeded.Fernet.Id, 2, null)]);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Equal(5, await UnitsAt(seeded, seeded.MainBar, seeded.Fernet));
    }

    // US-51, criterion 2: nothing moves, and the customer hears about the combo.
    [Fact]
    public async Task Confirm_WhenNoBarHasAComponent_RefusesNamingTheComboAndTakesNothing()
    {
        Seeded seeded = await Seed(ice: 0);

        Result<ConfirmedOrder> result = await Confirm(seeded, [new OrderLineRequest(seeded.Combo.Id, 1, null)]);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Contains("Combo Fernet", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(10, await UnitsAt(seeded, seeded.MainBar, seeded.Fernet));
    }

    [Fact]
    public async Task Confirm_WhenAComponentWasSwitchedOff_RefusesNamingTheCombo()
    {
        Seeded seeded = await Seed();
        await using (DrinkItDbContext write = sql.CreateContext(seeded.Venue.Id))
        {
            (await write.Products.SingleAsync(product => product.Id == seeded.Ice.Id)).MarkUnavailable();
            await write.SaveChangesAsync();
        }

        Result<ConfirmedOrder> result = await Confirm(seeded, [new OrderLineRequest(seeded.Combo.Id, 1, null)]);

        Assert.Equal(ConfirmOrderHandler.NotOnTheMenu.Code, result.Error!.Code);
        Assert.Contains("Combo Fernet", result.Error.Message, StringComparison.Ordinal);
    }

    // US-51, criterion 3: the recipe frozen when it was confirmed is what comes
    // off the bar when the till collects it.
    [Fact]
    public async Task Collect_WhenTheComboChangedAfterItWasConfirmed_TakesWhatItFroze()
    {
        Seeded seeded = await Seed();
        Result<ConfirmedOrder> confirmed = await Confirm(seeded, [new OrderLineRequest(seeded.Combo.Id, 1, null)], PaymentMethod.Cash);

        Assert.Equal(10, await UnitsAt(seeded, seeded.MainBar, seeded.Fernet));

        await using (DrinkItDbContext write = sql.CreateContext(seeded.Venue.Id))
        {
            Result<ProductSummary> changed = await new ReplaceComboComponentsHandler(new ProductRepository(write), new ProductsForOrdering(write))
                .HandleAsync(new ReplaceComboComponentsCommand(seeded.Combo.Id, [new ComboPart(seeded.Ice.Id, 3)]), CancellationToken.None);

            Assert.True(changed.IsSuccess);
        }

        Result<OrderStatus> collected = await Collect(seeded, confirmed.Value.Code);

        Assert.True(collected.IsSuccess);
        Assert.Equal(8, await UnitsAt(seeded, seeded.MainBar, seeded.Fernet));
        Assert.Equal(9, await UnitsAt(seeded, seeded.MainBar, seeded.Ice));
    }

    // The crash this story had to fix: the name was looked up among the order's
    // own lines, and a component is not one.
    [Fact]
    public async Task Collect_WhenAComponentRanOutMeanwhile_RefusesNamingTheCombo()
    {
        Seeded seeded = await Seed(ice: 1);
        Result<ConfirmedOrder> confirmed = await Confirm(seeded, [new OrderLineRequest(seeded.Combo.Id, 1, null)], PaymentMethod.Cash, key: "cash");
        await Confirm(seeded, [new OrderLineRequest(seeded.Ice.Id, 1, null)], key: "the-last-ice");

        Result<OrderStatus> collected = await Collect(seeded, confirmed.Value.Code);

        Assert.Equal(CashierErrors.SoldOut.Code, collected.Error!.Code);
        Assert.Contains("Combo Fernet", collected.Error.Message, StringComparison.Ordinal);
    }

    // US-51, criterion 4.
    [Fact]
    public async Task NightStock_WhenACombosSold_CountsItAgainstItsComponentsAndDoesNotListIt()
    {
        Seeded seeded = await Seed();
        await Confirm(seeded, [new OrderLineRequest(seeded.Combo.Id, 2, null), new OrderLineRequest(seeded.Fernet.Id, 1, null)]);

        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);
        NightStockSheet sheet = (await new NightStockQueries(read, new FixedClock(Tonight)).GetAsync(seeded.Night.Id, CancellationToken.None))!;
        IReadOnlyList<NightStockLine> lines = sheet.Bars.Single().Lines;

        Assert.Equal(5, lines.Single(line => line.ProductId == seeded.Fernet.Id).Sold);
        Assert.Equal(2, lines.Single(line => line.ProductId == seeded.Ice.Id).Sold);
        Assert.DoesNotContain(lines, line => line.ProductId == seeded.Combo.Id);
    }

    [Fact]
    public async Task AdjustNightStock_WhenItIsACombo_ThrowsItHasNoStock()
    {
        Seeded seeded = await Seed();
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        Night night = await context.Nights.SingleAsync();
        Product combo = await context.Products.SingleAsync(product => product.Id == seeded.Combo.Id);

        DomainException error = Assert.Throws<DomainException>(() => night.AdjustStock(seeded.MainBar, combo, 5, Tonight));

        Assert.Equal(Product.ErrorCodes.ComboHasNoStock, error.Code);
    }

    // US-52, criterion 1.
    [Fact]
    public async Task Menu_WhenOneBarCanMakeTheCombo_OffersItWithWhatItBrings()
    {
        Seeded seeded = await Seed();

        MenuItem combo = await TheComboOnTheMenu(seeded);

        Assert.True(combo.IsOrderable);
        Assert.Contains(combo.Components, part => part.Name == "Fernet" && part.Quantity == 2);
        Assert.Contains(combo.Components, part => part.Name == "Hielo" && part.Quantity == 1);
    }

    // US-52, criterion 2: each part somewhere is not enough, one bar has to have it all.
    [Fact]
    public async Task Menu_WhenTheComponentsAreInDifferentBars_ShowsItSoldOut()
    {
        Seeded seeded = await Seed(ice: 0);
        await using (DrinkItDbContext write = sql.CreateContext(seeded.Venue.Id))
        {
            StaffUser other = StaffUser.Create(seeded.Venue.Id, "other-bar", "hash", StaffRole.Kds);
            write.StaffUsers.Add(other);
            write.BarStocks.Add(new BarStock { VenueId = seeded.Venue.Id, NightId = seeded.Night.Id, KdsId = other.Id, ProductId = seeded.Ice.Id, Units = 10 });
            await write.SaveChangesAsync();
        }

        Assert.False((await TheComboOnTheMenu(seeded)).IsOrderable);
    }

    [Fact]
    public async Task Menu_WhenABarHasLessThanOneComboTakes_ShowsItSoldOut()
    {
        Assert.False((await TheComboOnTheMenu(await Seed(fernet: 1))).IsOrderable);
    }

    [Fact]
    public async Task Menu_WhenAComponentWasTakenOffTheMenu_ShowsItSoldOut()
    {
        Seeded seeded = await Seed();
        await using (DrinkItDbContext write = sql.CreateContext(seeded.Venue.Id))
        {
            (await write.Products.SingleAsync(product => product.Id == seeded.Ice.Id)).Deactivate();
            await write.SaveChangesAsync();
        }

        Assert.False((await TheComboOnTheMenu(seeded)).IsOrderable);
    }

    // US-53.
    [Fact]
    public async Task KdsQueue_WhenAnOrderHasACombo_ShowsWhatItWasMadeOfWhenConfirmed()
    {
        Seeded seeded = await Seed();
        await Confirm(seeded, [new OrderLineRequest(seeded.Combo.Id, 1, "sin hielo")]);

        await using (DrinkItDbContext write = sql.CreateContext(seeded.Venue.Id))
        {
            await new ReplaceComboComponentsHandler(new ProductRepository(write), new ProductsForOrdering(write))
                .HandleAsync(new ReplaceComboComponentsCommand(seeded.Combo.Id, [new ComboPart(seeded.Ice.Id, 3)]), CancellationToken.None);
        }

        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);
        KdsQueueOrderItem item = (await new KdsQueueQueries(read, new TheStaffIs(seeded.MainBar.Id, "main-bar")).GetQueueAsync(CancellationToken.None))
            .Single().OrderItems.Single();

        Assert.Equal("Combo Fernet", item.ProductName);
        Assert.Contains(item.Components, part => part.ProductName == "Fernet" && part.Quantity == 2);
        Assert.Contains(item.Components, part => part.ProductName == "Hielo" && part.Quantity == 1);
    }

    // The invariant of the whole model: nothing here crosses venues.
    [Fact]
    public async Task CreateCombo_WhenAComponentIsAnotherVenues_FailsAsNotFound()
    {
        Seeded mine = await Seed();
        Seeded theirs = await Seed();

        await using DrinkItDbContext context = sql.CreateContext(mine.Venue.Id);
        Result<ProductSummary> result = await new CreateComboHandler(
            new ProductRepository(context),
            new CategoryRepository(context),
            new ProductsForOrdering(context),
            new FixedVenue(mine.Venue.Id))
            .HandleAsync(new CreateComboCommand("Robado", null, null, 100m, mine.CategoryId, [new ComboPart(theirs.Fernet.Id, 2)]), CancellationToken.None);

        Assert.Equal(ProductErrors.ComboComponentNotFound, result.Error);
    }

    private async Task<MenuItem> TheComboOnTheMenu(Seeded seeded)
    {
        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);

        return (await new ProductQueries(read).ListForMenuAsync(seeded.Night.Id, CancellationToken.None))
            .Single(item => item.Id == seeded.Combo.Id);
    }

    private async Task<Result<ConfirmedOrder>> Confirm(
        Seeded seeded,
        OrderLineRequest[] lines,
        PaymentMethod method = PaymentMethod.Digital,
        string key = "a-key")
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);
        FixedVenue current = new(seeded.Venue.Id);

        return await new ConfirmOrderHandler(
            new OrderRepository(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance),
            new ProductsForOrdering(context),
            new BarsForOrdering(context),
            new OrderCodeSequence(context, current),
            [new DigitalPaymentStrategy(new FixedClock(Tonight)), new CashPaymentStrategy()],
            current,
            new UnderwayNightLookup(context),
            new FixedClock(Tonight),
            new ActivePromotions(context),
            new StampCardRepository(context),
            new StampCardQueries(context))
            .HandleAsync(new ConfirmOrderCommand("María Quadro", method, key, lines), CancellationToken.None);
    }

    private async Task<Result<OrderStatus>> Collect(Seeded seeded, string code)
    {
        await using DrinkItDbContext context = sql.CreateContext(seeded.Venue.Id);

        return await new CollectCashHandler(
            new OrderRepository(context, new NoDispatcher(), NullLogger<OrderRepository>.Instance),
            new BarsForOrdering(context),
            new UnderwayNightLookup(context),
            new FixedClock(Tonight),
            new TheStaffIs(seeded.Till.Id, "till-1"))
            .HandleAsync(code, CancellationToken.None);
    }

    private async Task<int> UnitsAt(Seeded seeded, StaffUser bar, Product product)
    {
        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);

        return await read.BarStocks
            .Where(stock => stock.NightId == seeded.Night.Id && stock.KdsId == bar.Id && stock.ProductId == product.Id)
            .Select(stock => stock.Units)
            .SingleAsync();
    }

    private async Task<int> GeneralStock(Seeded seeded, Product product)
    {
        await using DrinkItDbContext read = sql.CreateContext(seeded.Venue.Id);

        return await read.Products.Where(row => row.Id == product.Id).Select(row => row.Stock).SingleAsync();
    }

    private async Task<Seeded> Seed(int fernet = 10, int ice = 10)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product fernetProduct = Product.Create(venue.Id, "Fernet", null, null, 4000m, fernet, category.Id);
        Product iceProduct = Product.Create(venue.Id, "Hielo", null, null, 500m, ice, category.Id);
        Product combo = Product.CreateCombo(venue.Id, "Combo Fernet", null, null, 9000m, category.Id, [(fernetProduct.Id, 2), (iceProduct.Id, 1)]);
        StaffUser bar = StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier);
        Night night = Night.Create(venue.Id, "Tonight", Tonight.AddHours(-3), Tonight.AddHours(4), [bar, till]);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.AddRange(fernetProduct, iceProduct);
        await seed.SaveChangesAsync();
        seed.Products.Add(combo);
        seed.StaffUsers.AddRange(bar, till);
        seed.Nights.Add(night);
        seed.BarStocks.AddRange(
            new BarStock { VenueId = venue.Id, NightId = night.Id, KdsId = bar.Id, ProductId = fernetProduct.Id, Units = fernet },
            new BarStock { VenueId = venue.Id, NightId = night.Id, KdsId = bar.Id, ProductId = iceProduct.Id, Units = ice });
        await seed.SaveChangesAsync();

        return new Seeded(venue, night, bar, till, fernetProduct, iceProduct, combo, category.Id);
    }

    private sealed record Seeded(Venue Venue, Night Night, StaffUser MainBar, StaffUser Till, Product Fernet, Product Ice, Product Combo, Guid CategoryId);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id { get; } = id;
    }

    private sealed class TheStaffIs(Guid id, string username) : ICurrentStaffUser
    {
        public Guid? Id => id;

        public string? Username => username;
    }

    private sealed class NoDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
