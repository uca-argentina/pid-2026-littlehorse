using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Nights;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

    // US-35, criterion 2, through the real lookup and the real table.
    [Fact]
    public async Task ConfirmOrder_WhenANightIsOn_StoresTheOrderInThatNight()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);

        await Confirm(venue, [new OrderLineRequest(gin.Id, 1, null)]);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Order stored = await check.Orders.SingleAsync();
        Night tonight = await check.Nights.SingleAsync();

        Assert.Equal(tonight.Id, stored.NightId);
    }

    // US-35, criterion 3: the night ended, and confirming is closed.
    [Fact]
    public async Task ConfirmOrder_WhenNoNightIsOn_StoresNothing()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20, withNight: false);

        Result<ConfirmedOrder> result = await Confirm(venue, [new OrderLineRequest(gin.Id, 1, null)]);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Assert.Equal(OrderErrors.NotTakingOrders, result.Error);
        Assert.Empty(await check.Orders.ToListAsync());
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
    /// Two people reaching for the same last drink at the same instant. Exactly
    /// one of them gets it: the other is told it ran out, and the shelf is left
    /// at zero rather than at minus one.
    /// </summary>
    /// <remarks>
    /// The review of 2026-09-17 found this open: the stock was read, lowered in
    /// memory and written with an UPDATE that named only the row, so both
    /// writers won and one drink was sold twice.
    /// </remarks>
    [Fact]
    public async Task ConfirmOrder_WhenTwoPeopleTakeTheLastDrinkAtOnce_OnlyOneGetsIt()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 1);

        Result<ConfirmedOrder>[] attempts = await Task.WhenAll(
            Confirm(venue, [new OrderLineRequest(gin.Id, 1, null)], key: "first"),
            Confirm(venue, [new OrderLineRequest(gin.Id, 1, null)], key: "second"));

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        Assert.Equal(1, attempts.Count(attempt => attempt.IsSuccess));
        Assert.Equal(1, await check.Orders.CountAsync());
        Assert.Equal(0, (await check.Products.SingleAsync(product => product.Id == gin.Id)).Stock);
    }

    /// <summary>
    /// Criterion 6 where it actually happens: a retry lands while the first
    /// attempt is still in flight, which is what a bad signal produces. Both are
    /// answered with the same order, and neither with a failure.
    /// </summary>
    [Fact]
    public async Task ConfirmOrder_WhenTheRetryArrivesBeforeTheFirstFinishes_BothGetTheSameOrder()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);

        Result<ConfirmedOrder>[] attempts = await Task.WhenAll(
            Confirm(venue, [new OrderLineRequest(gin.Id, 2, null)], key: "the-same-key"),
            Confirm(venue, [new OrderLineRequest(gin.Id, 2, null)], key: "the-same-key"));

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        Assert.All(attempts, attempt => Assert.True(attempt.IsSuccess));
        Assert.Equal(attempts[0].Value.Code, attempts[1].Value.Code);
        Assert.Equal(1, await check.Orders.CountAsync());
        Assert.Equal(18, (await check.Products.SingleAsync(product => product.Id == gin.Id)).Stock);
    }

    // US-15: confirming an order is what raises OrderQueued, and the KDS board
    // learns about it through the same dispatcher every other use case will.
    [Fact]
    public async Task ConfirmOrder_WhenItReachesTheQueue_NotifiesTheKdsBoard()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);
        SpyDispatcher spy = new();

        await Confirm(venue, [new OrderLineRequest(gin.Id, 1, null)], dispatcher: spy);

        OrderQueued raised = Assert.IsType<OrderQueued>(Assert.Single(spy.Received));
        Assert.Equal(venue.Id, raised.VenueId);
    }

    // A dropped notification is not a failed order: the money is taken and the
    // drink is queued either way, and losing the order over it would be worse
    // than a tablet that catches up on its next reload.
    [Fact]
    public async Task ConfirmOrder_WhenNotifyingTheBoardFails_StillSucceeds()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);

        Result<ConfirmedOrder> result = await Confirm(
            venue,
            [new OrderLineRequest(gin.Id, 1, null)],
            dispatcher: new ThrowingDispatcher());

        Assert.True(result.IsSuccess);
    }

    // Still succeeds, but not in silence: an order the board never heard of is
    // a customer waiting on a drink nobody is making, and the log is the only
    // place left that can tell anybody why.
    [Fact]
    public async Task ConfirmOrder_WhenNotifyingTheBoardFails_LogsIt()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);
        SpyLogger logger = new();

        await Confirm(
            venue,
            [new OrderLineRequest(gin.Id, 1, null)],
            dispatcher: new ThrowingDispatcher(),
            logger: logger);

        (LogLevel level, Exception? exception) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.IsType<InvalidOperationException>(exception);
    }

    // The order is committed by then, so the customer closing the app right
    // after paying must not take the board's notification down with it.
    [Fact]
    public async Task ConfirmOrder_WhenItReachesTheQueue_NotifiesWithoutTheRequestsCancellation()
    {
        (Venue venue, Product gin) = await AVenueSelling("Gin Tonic", stock: 20);
        SpyDispatcher spy = new();
        using CancellationTokenSource request = new();

        await Confirm(
            venue,
            [new OrderLineRequest(gin.Id, 1, null)],
            dispatcher: spy,
            cancellationToken: request.Token);

        Assert.False(spy.ReceivedToken.CanBeCanceled);
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
        string name = "María Quadro",
        IDomainEventDispatcher? dispatcher = null,
        ILogger<OrderRepository>? logger = null,
        CancellationToken cancellationToken = default)
    {
        await using DrinkItDbContext context = sql.CreateContext(venue.Id);
        FixedVenue current = new(venue.Id);

        ConfirmOrderHandler handler = new(
            new OrderRepository(
                context,
                dispatcher ?? new SpyDispatcher(),
                logger ?? NullLogger<OrderRepository>.Instance),
            new ProductsForOrdering(context),
            new OrderCodeSequence(context, current),
            [new DigitalPaymentStrategy(new FixedClock(Tonight))],
            current,
            new UnderwayNightLookup(context),
            new FixedClock(Tonight));

        return await handler.HandleAsync(
            new ConfirmOrderCommand(name, PaymentMethod.Digital, key, lines),
            cancellationToken);
    }

    /// <summary>A venue with one drink, and by default a night on around <see cref="Tonight"/>.</summary>
    private async Task<(Venue Venue, Product Product)> AVenueSelling(string drink, int stock, bool withNight = true)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product product = Product.Create(venue.Id, drink, null, null, 4500m, stock, category.Id);
        StaffUser[] crew =
        [
            StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds),
            StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier),
        ];

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(product);
        seed.StaffUsers.AddRange(crew);
        if (withNight) seed.Nights.Add(Night.Create(venue.Id, "Tonight", Tonight.AddHours(-3), Tonight.AddHours(4), crew));
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

    private sealed class SpyDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Received { get; } = [];

        public CancellationToken ReceivedToken { get; private set; }

        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            Received.AddRange(events);
            ReceivedToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Hand-written rather than Microsoft's FakeLogger: that one is its own
    /// NuGet package, and a list of what was logged is all this needs.
    /// </summary>
    private sealed class SpyLogger : ILogger<OrderRepository>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }

    private sealed class ThrowingDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The board is unreachable.");
    }
}
