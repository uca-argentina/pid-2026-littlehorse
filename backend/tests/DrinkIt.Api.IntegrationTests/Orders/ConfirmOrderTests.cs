using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// Confirming an order against a real database. What is proved here cannot be
/// proved in memory: that the order and the stock it sold are written together,
/// that two venues never share a code, and that two customers confirming at the
/// same instant get different ones.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class ConfirmOrderTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Tonight = new(2026, 9, 17, 2, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConfirmOrder_WhenItIsTheVenuesFirst_StartsTheCodesAtTheBeginning()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);

        Result<ConfirmedOrder> result = await Confirm(venue, [new OrderLineRequest(gin.Id, 2, "sin hielo")]);

        Assert.True(result.IsSuccess);
        Assert.Equal("A-0000", result.Value.Code);
        Assert.Equal(9000m, result.Value.Total);
    }

    [Fact]
    public async Task ConfirmOrder_WhenAnotherFollows_HandsOutTheNextCode()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);

        await Confirm(venue, [new OrderLineRequest(gin.Id, 1, null)], key: "one");
        Result<ConfirmedOrder> second = await Confirm(venue, [new OrderLineRequest(gin.Id, 1, null)], key: "two");

        Assert.Equal("A-0001", second.Value.Code);
    }

    /// <summary>
    /// Criterion 4, and the reason the sequence lives in the database: ten
    /// customers confirming at the same instant, and no two of them holding the
    /// same code.
    /// </summary>
    [Fact]
    public async Task ConfirmOrder_WhenTenPeopleConfirmAtOnce_NobodySharesACode()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 100);

        Result<ConfirmedOrder>[] results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(i =>
                Confirm(venue, [new OrderLineRequest(gin.Id, 1, null)], key: $"key-{i}")));

        string[] codes = [.. results.Where(r => r.IsSuccess).Select(r => r.Value.Code)];

        Assert.Equal(10, codes.Length);
        Assert.Equal(10, codes.Distinct(StringComparer.Ordinal).Count());
    }

    // The order and the stock it sold are one write. An order that exists with
    // its drinks still on the shelf is the outcome this rules out.
    [Fact]
    public async Task ConfirmOrder_WhenItSucceeds_StoresTheOrderAndTheSoldStockTogether()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);

        await Confirm(venue, [new OrderLineRequest(gin.Id, 3, null)]);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        Order stored = await check.Orders.Include(order => order.Items).SingleAsync();

        Assert.Equal(OrderStatus.Queued, stored.Status);
        Assert.Equal(Tonight, stored.PaidAt);
        Assert.Equal("Gin Tonic", stored.Items.Single().ProductName);
        Assert.Equal(17, (await check.Products.SingleAsync(product => product.Id == gin.Id)).Stock);
    }

    // Criterion 6 against a real database: the same key twice is one order and
    // one stock movement, whatever the network did in between.
    [Fact]
    public async Task ConfirmOrder_WhenTheSameKeyArrivesTwice_WritesOneOrder()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);

        Result<ConfirmedOrder> first = await Confirm(venue, [new OrderLineRequest(gin.Id, 2, null)], key: "same");
        Result<ConfirmedOrder> second = await Confirm(venue, [new OrderLineRequest(gin.Id, 2, null)], key: "same");

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        Assert.Equal(first.Value.Code, second.Value.Code);
        Assert.Equal(1, await check.Orders.CountAsync());
        Assert.Equal(18, (await check.Products.SingleAsync(product => product.Id == gin.Id)).Stock);
    }

    /// <summary>
    /// The invariant CLAUDE.md calls the most important one in the data model,
    /// on the newest table that carries a VenueId.
    /// </summary>
    [Fact]
    public async Task Orders_WhenReadFromAnotherVenue_AreNotVisible()
    {
        (Venue mine, Product myGin) = await AVenueSelling("Gin Tonic", stock: 20);
        (Venue theirs, Product theirGin) = await AVenueSelling("Gin Tonic", stock: 20);

        await Confirm(mine, [new OrderLineRequest(myGin.Id, 1, null)]);
        await Confirm(theirs, [new OrderLineRequest(theirGin.Id, 1, null)]);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.Equal(1, await asMine.Orders.CountAsync());
        Assert.Equal(mine.Id, (await asMine.Orders.SingleAsync()).VenueId);
    }

    // Codes run per venue, so both of them start at A-0000 the same night and
    // never meet. The unique index is composite with VenueId for this.
    [Fact]
    public async Task ConfirmOrder_WhenTwoVenuesSellAtOnce_BothStartAtTheBeginning()
    {
        (Venue mine, Product myGin) = await AVenueSelling("Gin Tonic", stock: 20);
        (Venue theirs, Product theirGin) = await AVenueSelling("Gin Tonic", stock: 20);

        Result<ConfirmedOrder> ours = await Confirm(mine, [new OrderLineRequest(myGin.Id, 1, null)]);
        Result<ConfirmedOrder> mine2 = await Confirm(theirs, [new OrderLineRequest(theirGin.Id, 1, null)]);

        Assert.Equal("A-0000", ours.Value.Code);
        Assert.Equal("A-0000", mine2.Value.Code);
    }

    // A product of another venue is not found, not forbidden: nothing here
    // tells a customer what anybody else sells.
    [Fact]
    public async Task ConfirmOrder_WhenTheDrinkBelongsToAnotherVenue_RejectsTheOrder()
    {
        (Venue mine, _) = await AVenueSelling("Gin Tonic", stock: 20);
        (_, Product theirGin) = await AVenueSelling("Gin Tonic", stock: 20);

        Result<ConfirmedOrder> result = await Confirm(mine, [new OrderLineRequest(theirGin.Id, 1, null)]);

        Assert.Equal(ConfirmOrderHandler.NotOnTheMenu.Code, result.Error!.Code);
    }

    [Fact]
    public async Task ConfirmOrder_WhenTheDrinkRanOut_WritesNothing()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 1);

        Result<ConfirmedOrder> result = await Confirm(venue, [new OrderLineRequest(gin.Id, 2, null)]);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        Assert.Equal(ConfirmOrderHandler.SoldOut.Code, result.Error!.Code);
        Assert.Equal(0, await check.Orders.CountAsync());
        Assert.Equal(1, (await check.Products.SingleAsync(product => product.Id == gin.Id)).Stock);
    }

    /// <summary>
    /// One handler over one context, the way a request gets it: every port in
    /// the use case shares the unit of work, which is what makes the order and
    /// its stock a single write.
    /// </summary>
    private async Task<Result<ConfirmedOrder>> Confirm(
        Venue venue,
        OrderLineRequest[] lines,
        string key = "a-key",
        string name = "María Quadro")
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        FixedVenue current = new(venue.Id);

        ConfirmOrderHandler handler = new(
            new OrderRepository(context),
            new ProductsForOrdering(context),
            new OrderCodeSequence(context, current),
            [new DigitalPaymentStrategy(new FixedClock(Tonight))],
            current);

        return await handler.HandleAsync(
            new ConfirmOrderCommand(name, PaymentMethod.Digital, key, lines),
            CancellationToken.None);
    }

    private async Task<(Venue Venue, Product Product)> AVenueSelling(string drink, int stock)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Product product = Product.Create(venue.Id, drink, null, null, 4500m, stock);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Products.Add(product);
        await seed.SaveChangesAsync();

        return (venue, product);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedVenue(Guid id) : ICurrentVenue
    {
        public Guid Id { get; } = id;
    }
}
