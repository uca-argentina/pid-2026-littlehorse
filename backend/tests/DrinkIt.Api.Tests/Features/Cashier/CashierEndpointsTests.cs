using DrinkIt.Api.Features.Cashier;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Waiter;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Cashier;

/// <summary>US-26: looking an order up at the till and collecting it, as the till's screen calls them.</summary>
public class CashierEndpointsTests
{
    private static readonly Guid Gin = Guid.CreateVersion7();

    private static readonly Guid Tonight = Guid.CreateVersion7();

    private const string Path = "/cashier/orders/K-4821";

    [Fact]
    public async Task CollectAsync_WhenTheOrderAwaitsCash_RespondsWithNoContent()
    {
        IResult result = await CashierEndpoints.CollectAsync("K-4821", ACollector(AnOrderWaitingForCash()), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/collect", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
    }

    // US-45, criterion 1: what is waiting for a waiter, and what one already has.
    [Fact]
    public async Task GetVipDeliveriesAsync_WhenTablesAreReady_ListsThemWithWhoHasThemAndNoWaiterId()
    {
        DateTimeOffset ready = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
        Tests.Features.Waiter.FakeWaiterQueries queries = new(
            new WaiterTableOrder(Guid.CreateVersion7(), 12, "Sofi", ready, null, null, null, [new WaiterOrderItem("Aperol Spritz", 1, null)]),
            new WaiterTableOrder(Guid.CreateVersion7(), 5, "Nico", ready, Guid.CreateVersion7(), "Martín", ready.AddSeconds(40), []));

        IResult result = await CashierEndpoints.GetVipDeliveriesAsync(queries, CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, "/cashier/vip-deliveries", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(12, response.Body[0].GetProperty("tableNumber").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, response.Body[0].GetProperty("takenByName").ValueKind);
        Assert.Equal("Aperol Spritz", response.Body[0].GetProperty("items")[0].GetProperty("productName").GetString());
        Assert.Equal("Martín", response.Body[1].GetProperty("takenByName").GetString());
        Assert.Equal(ready.AddSeconds(40), response.Body[1].GetProperty("takenAt").GetDateTimeOffset());
        // The till watches; it never learns which account a waiter is.
        Assert.False(response.Body[1].TryGetProperty("waiterId", out _));
    }

    [Fact]
    public async Task CollectAsync_WhenNoOrderHereHasThatCode_RespondsWithNotFound()
    {
        IResult result = await CashierEndpoints.CollectAsync("K-9999", ACollector(AnOrderWaitingForCash()), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/collect", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:cashier:order-not-found", response.Text("type"));
    }

    // Criterion 3: a second tap is a conflict the screen names, never a second charge.
    [Fact]
    public async Task CollectAsync_WhenTheOrderWasAlreadyPaid_RespondsWithConflict()
    {
        Order order = AnOrderWaitingForCash();
        CollectCashHandler collector = ACollector(order);
        await collector.HandleAsync("K-4821", CancellationToken.None);

        IResult result = await CashierEndpoints.CollectAsync("K-4821", collector, CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/collect", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:cashier:already-paid", response.Text("type"));
    }

    // US-36: by the time the money is in, no bar has the drinks any more. The
    // till names the one missing, and nothing was charged.
    [Fact]
    public async Task CollectAsync_WhenNoBarHasTheDrinks_RespondsWithConflictNamingTheDrink()
    {
        CollectCashHandler collector = new(new Fake.Orders(AnOrderWaitingForCash()), new Fake.Bars(units: 0), new Fake.Nights(Tonight), TimeProvider.System, new Fake.Cashier());

        IResult result = await CashierEndpoints.CollectAsync("K-4821", collector, CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/collect", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:cashier:sold-out", response.Text("type"));
        Assert.Equal("Gin Tonic", response.Text("productName"));
    }

    // Every attempt lost the race for the same drink: the order is still there,
    // waiting, and the till tries again — not "no order with that code".
    [Fact]
    public async Task CollectAsync_WhenEveryAttemptLosesTheRace_RespondsWithConflictToTryAgain()
    {
        CollectCashHandler collector = new(
            new Fake.OrdersAlwaysLosingTheRace(AnOrderWaitingForCash),
            new Fake.Bars(units: 10),
            new Fake.Nights(Tonight),
            TimeProvider.System,
            new Fake.Cashier());

        IResult result = await CashierEndpoints.CollectAsync("K-4821", collector, CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/collect", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:stock-moved", response.Text("type"));
    }

    // The order's night is over: nothing to collect against, and the till says why.
    [Fact]
    public async Task CollectAsync_WhenTheOrdersNightIsOver_RespondsWithConflict()
    {
        CollectCashHandler collector = new(new Fake.Orders(AnOrderWaitingForCash()), new Fake.Bars(units: 10), new Fake.Nights(null), TimeProvider.System, new Fake.Cashier());

        IResult result = await CashierEndpoints.CollectAsync("K-4821", collector, CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/collect", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:cashier:night-over", response.Text("type"));
    }

    // US-23: the customer left without paying, and the till cancels it.
    [Fact]
    public async Task CancelAsync_WhenTheOrderAwaitsCash_RespondsWithNoContent()
    {
        Order order = AnOrderWaitingForCash();

        IResult result = await CashierEndpoints.CancelAsync("K-4821", new CancelAtTillHandler(new Fake.Orders(order)), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/cancel", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
        Assert.Equal(OrderStatus.Canceled, order.Status);
    }

    [Fact]
    public async Task CancelAsync_WhenNoOrderHereHasThatCode_RespondsWithNotFound()
    {
        IResult result = await CashierEndpoints.CancelAsync("K-9999", new CancelAtTillHandler(new Fake.Orders(AnOrderWaitingForCash())), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/cancel", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:cashier:order-not-found", response.Text("type"));
    }

    // Paid is the bar's: the same conflict the screen already names for a
    // second collection.
    [Fact]
    public async Task CancelAsync_WhenTheOrderWasAlreadyPaid_RespondsWithConflict()
    {
        Order order = AnOrderWaitingForCash();
        order.CollectCash(DateTimeOffset.UtcNow, "laura.caja");
        order.Enqueue(Guid.CreateVersion7());

        IResult result = await CashierEndpoints.CancelAsync("K-4821", new CancelAtTillHandler(new Fake.Orders(order)), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/cancel", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:cashier:already-paid", response.Text("type"));
    }

    // Criterion 1: the drinks and what to charge.
    [Fact]
    public async Task FindAsync_WhenTheCodeIsThisVenues_RespondsWithTheOrder()
    {
        CashierOrder order = new(
            "K-4821", "María Quadro", OrderStatus.AwaitingPayment, 9000m, null, null,
            [new CashierOrderItem("Gin Tonic", 2, "sin hielo", 4500m)]);

        IResult result = await CashierEndpoints.FindAsync("K-4821", new Fake.Queries(order), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("AwaitingPayment", response.Text("status"));
    }

    [Fact]
    public async Task FindAsync_WhenNoOrderHereHasThatCode_RespondsWithNotFound()
    {
        IResult result = await CashierEndpoints.FindAsync("K-9999", new Fake.Queries(), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:cashier:order-not-found", response.Text("type"));
    }

    // The QR on the customer's phone: the token, sent in the body so it never
    // lands in a URL or an access log.
    [Fact]
    public async Task ScanAsync_WhenTheTokenIsThisVenues_RespondsWithTheOrder()
    {
        IResult result = await CashierEndpoints.ScanAsync(
            new CashierScanRequest("the-token-of-K-4821"), new Fake.Queries(AnAwaitingOrder()), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, "/cashier/scan", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("K-4821", response.Text("code"));
    }

    [Fact]
    public async Task ScanAsync_WhenTheReadIsNobodys_RespondsWithNotFound()
    {
        IResult result = await CashierEndpoints.ScanAsync(
            new CashierScanRequest("garbage"), new Fake.Queries(AnAwaitingOrder()), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, "/cashier/scan", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:cashier:order-not-found", response.Text("type"));
    }

    [Fact]
    public async Task GetMyCollectionsAsync_Always_RespondsWithWhatTheHandlerFound()
    {
        CashierOrder collected = AnAwaitingOrder() with { Status = OrderStatus.Queued, PaidAt = DateTimeOffset.UtcNow };

        IResult result = await CashierEndpoints.GetMyCollectionsAsync(
            new MyCollectionsHandler(new Fake.Queries(collected), new Fake.Cashier(), TimeProvider.System),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, "/cashier/collections", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("K-4821", response.Body[0].GetProperty("code").GetString());
    }

    private static CashierOrder AnAwaitingOrder() => new(
        "K-4821", "María Quadro", OrderStatus.AwaitingPayment, 9000m, null, null,
        [new CashierOrderItem("Gin Tonic", 2, "sin hielo", 4500m)]);

    private static CollectCashHandler ACollector(params Order[] stored) =>
        new(new Fake.Orders(stored), new Fake.Bars(units: 10), new Fake.Nights(Tonight), TimeProvider.System, new Fake.Cashier());

    private static Order AnOrderWaitingForCash()
    {
        Order order = Order.Place(Guid.CreateVersion7(), Tonight, "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Gin, "Gin Tonic", 4500m, 1, null)]);
        order.AwaitPayment(PaymentMethod.Cash);

        return order;
    }

    private static class Fake
    {
        public sealed class Orders(params Order[] stored) : IOrderRepository
        {
            public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
                Task.FromResult(stored.SingleOrDefault(order => order.Code == code));

            public Task<Order?> GetForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
                Task.FromResult(stored.SingleOrDefault(order => order.TrackingToken == token));

            public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(order);

            public Task<Result<Order>> SaveCancellationAsync(Order order, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(order);

            public Task<Result<Order>> SaveCollectionAsync(Order order, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(order);

            public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till never places an order.");

            public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till never places an order.");
        }

        /// <summary>
        /// Every collection refused because the bar sold the drink first. Like
        /// the real one after a refused write, the order is found again as it
        /// is in the database: still waiting for cash.
        /// </summary>
        public sealed class OrdersAlwaysLosingTheRace(Func<Order> asStored) : IOrderRepository
        {
            public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
                Task.FromResult<Order?>(asStored());

            public Task<Result<Order>> SaveCollectionAsync(Order order, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(OrderErrors.StockMoved);

            public Task<Order?> GetForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
                throw new NotSupportedException("Collecting finds the order by its code.");

            public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken) =>
                throw new NotSupportedException("Collecting saves through SaveCollectionAsync.");

            public Task<Result<Order>> SaveCancellationAsync(Order order, CancellationToken cancellationToken) =>
                throw new NotSupportedException("Collecting saves through SaveCollectionAsync.");

            public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till never places an order.");

            public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till never places an order.");
        }

        /// <summary>The night on now, or none.</summary>
        public sealed class Nights(Guid? underway) : IUnderwayNightLookup
        {
            public Task<Guid?> FindIdAsync(DateTimeOffset at, CancellationToken cancellationToken) => Task.FromResult(underway);
        }

        /// <summary>One bar of the night, holding that many of every drink.</summary>
        public sealed class Bars(int units) : IBarsForOrdering
        {
            public Task<IReadOnlyList<BarSupply>> GetAsync(Guid nightId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<BarSupply>>(
                    [new BarSupply(Guid.CreateVersion7(), 0, productIds.ToDictionary(id => id, _ => units))]);
        }

        public sealed class Cashier : ICurrentStaffUser
        {
            public string? Username => "laura.caja";

            public Guid? Id => null;
        }

        public sealed class Queries(params CashierOrder[] stored) : ICashierQueries
        {
            public Task<IReadOnlyList<CashierOrder>> GetAwaitingPaymentAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<CashierOrder>>(stored);

            public Task<CashierOrder?> FindAsync(string code, CancellationToken cancellationToken) =>
                Task.FromResult(stored.SingleOrDefault(order => order.Code == code));

            public Task<CashierOrder?> FindByTokenAsync(string token, CancellationToken cancellationToken) =>
                Task.FromResult(stored.FirstOrDefault(order => token == "the-token-of-" + order.Code));

            public Task<IReadOnlyList<CashierOrder>> GetCollectedByAsync(string cashier, DateTimeOffset since, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<CashierOrder>>(stored.Where(order => order.PaidAt is not null).ToList());
        }
    }
}
