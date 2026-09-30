using DrinkIt.Api.Features.Cashier;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Cashier;

/// <summary>US-26: looking an order up at the till and collecting it, as the till's screen calls them.</summary>
public class CashierEndpointsTests
{
    private const string Path = "/cashier/orders/K-4821";

    [Fact]
    public async Task CollectAsync_WhenTheOrderAwaitsCash_RespondsWithNoContent()
    {
        IResult result = await CashierEndpoints.CollectAsync("K-4821", ACollector(AnOrderWaitingForCash()), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/collect", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
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
        new(new Fake.Orders(stored), TimeProvider.System, new Fake.Cashier());

    private static Order AnOrderWaitingForCash()
    {
        Order order = Order.Place(
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.AwaitPayment(PaymentMethod.Cash);

        return order;
    }

    private static class Fake
    {
        public sealed class Orders(params Order[] stored) : IOrderRepository
        {
            public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
                Task.FromResult(stored.SingleOrDefault(order => order.Code == code));

            public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
                Task.FromResult(stored.SingleOrDefault(order => order.TrackingToken == token));

            public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(order);

            public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till never places an order.");

            public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till never places an order.");

            public Task<Order?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till finds orders by code or by token.");

            public Task<IReadOnlyList<Order>> GetAwaitingPaymentCreatedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till never expires an order.");

            public Task SaveReturningStockAsync(Order order, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The till never cancels an unpaid order.");
        }

        public sealed class Cashier : ICurrentStaffUser
        {
            public string? Username => "laura.caja";
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
