using DrinkIt.Api.Features.Waiter;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Waiter;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Waiter;

public class WaiterEndpointsTests
{
    private const string Path = "/waiter/tables";

    private static readonly Guid Martin = Guid.CreateVersion7();

    private static readonly Guid Sofia = Guid.CreateVersion7();

    private static readonly DateTimeOffset Midnight = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    private static WaiterTableOrder AFreeTable(int number) =>
        new(Guid.CreateVersion7(), number, "Nico", Midnight, null, null, null, [new WaiterOrderItem("Gin Tonic", 2, "sin hielo")]);

    private static WaiterTableOrder TakenBy(Guid waiter, string name, int number) =>
        new(Guid.CreateVersion7(), number, "Sofi", Midnight, waiter, name, Midnight.AddSeconds(40), [new WaiterOrderItem("Cerveza", 3, null)]);

    private static Order AReadyTableOrder()
    {
        Order order = Order.Place(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "María Quadro", OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.ServeAtTable(Guid.CreateVersion7(), 5);
        order.PayFromBalance(Midnight, order.Total);
        order.Enqueue(Guid.CreateVersion7());
        order.StartPreparing(Midnight);
        order.MarkReady(Midnight);
        order.ClearDomainEvents();

        return order;
    }

    // Pins the shape: the phone's client is generated from it. The waiter's own
    // id is not in it — "mine" is decided here, from the token.
    [Fact]
    public async Task ListAsync_WhenTheVenueHasTablesReady_RespondsWithWhatTheWaiterNeedsToCarryThem()
    {
        FakeWaiterQueries queries = new(AFreeTable(12));

        IResult result = await WaiterEndpoints.ListAsync(queries, new Fake.TheWaiterIs(Martin), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        System.Text.Json.JsonElement table = response.Body[0];
        Assert.NotEqual(Guid.Empty, table.GetProperty("orderId").GetGuid());
        Assert.Equal(12, table.GetProperty("tableNumber").GetInt32());
        Assert.Equal("Nico", table.GetProperty("customerName").GetString());
        Assert.Equal(Midnight, table.GetProperty("readyAt").GetDateTimeOffset());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, table.GetProperty("takenByName").ValueKind);
        Assert.False(table.GetProperty("takenByMe").GetBoolean());
        System.Text.Json.JsonElement item = table.GetProperty("items")[0];
        Assert.Equal("Gin Tonic", item.GetProperty("productName").GetString());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal("sin hielo", item.GetProperty("note").GetString());
        Assert.False(table.TryGetProperty("waiterId", out _));
    }

    // Criterion 2: "Las que tomaste" and "Las lleva otro" are told apart by the token.
    [Fact]
    public async Task ListAsync_WhenSomeAreTaken_SaysWhichOnesAreTheCallersOwn()
    {
        FakeWaiterQueries queries = new(TakenBy(Martin, "Martín", 5), TakenBy(Sofia, "Sofía", 3));

        IResult result = await WaiterEndpoints.ListAsync(queries, new Fake.TheWaiterIs(Martin), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.True(response.Body[0].GetProperty("takenByMe").GetBoolean());
        Assert.Equal("Martín", response.Body[0].GetProperty("takenByName").GetString());
        Assert.False(response.Body[1].GetProperty("takenByMe").GetBoolean());
        Assert.Equal("Sofía", response.Body[1].GetProperty("takenByName").GetString());
    }

    [Fact]
    public async Task TakeAsync_WhenTheOrderIsFree_RespondsWithItNowTheirs()
    {
        Order order = AReadyTableOrder();

        IResult result = await WaiterEndpoints.TakeAsync(order.Id, HandlerFor(order), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{order.Id}/take", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(order.Id, response.Body.GetProperty("orderId").GetGuid());
        Assert.Equal(5, response.Body.GetProperty("tableNumber").GetInt32());
        Assert.Equal("Martín", response.Body.GetProperty("waiterName").GetString());
    }

    [Fact]
    public async Task TakeAsync_WhenThereIsNoSuchOrder_RespondsNotFound()
    {
        Guid id = Guid.CreateVersion7();

        IResult result = await WaiterEndpoints.TakeAsync(id, HandlerFor(), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{id}/take", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:waiter:order-not-found", response.Text("type"));
    }

    // Criterion 3: the order is there, it just is not free any more, and the
    // phone reloads to see whose it is.
    [Fact]
    public async Task TakeAsync_WhenAnotherWaiterHasIt_RespondsWithConflict()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Sofia, "Sofía", Midnight);

        IResult result = await WaiterEndpoints.TakeAsync(order.Id, HandlerFor(order), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{order.Id}/take", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:waiter:already-taken", response.Text("type"));
    }

    [Fact]
    public async Task TakeAsync_WhenAnotherWaiterSavesFirst_RespondsWithConflict()
    {
        Order order = AReadyTableOrder();

        IResult result = await WaiterEndpoints.TakeAsync(order.Id, HandlerFor(order, losesTheRace: true), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{order.Id}/take", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:changed-meanwhile", response.Text("type"));
    }

    // A move the order cannot make is the domain's: the global handler answers.
    [Fact]
    public async Task TakeAsync_WhenTheOrderIsNotReady_LetsTheDomainExceptionThrough()
    {
        Order order = AReadyTableOrder();
        order.ReturnToPreparation();

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => WaiterEndpoints.TakeAsync(order.Id, HandlerFor(order), CancellationToken.None));

        Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
    }

    // US-44, criterion 1: delivered, and the phone reloads the list.
    [Fact]
    public async Task DeliverAsync_WhenTheWaiterCarriesIt_RespondsNoContent()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Martin, "Martín", Midnight);

        IResult result = await WaiterEndpoints.DeliverAsync(order.Id, DeliverHandlerFor(order), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{order.Id}/deliver", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    // Criterion 2: not theirs to hand over.
    [Fact]
    public async Task DeliverAsync_WhenAnotherWaiterCarriesIt_RespondsWithConflict()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Sofia, "Sofía", Midnight);

        IResult result = await WaiterEndpoints.DeliverAsync(order.Id, DeliverHandlerFor(order), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{order.Id}/deliver", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:waiter:not-yours", response.Text("type"));
        Assert.Equal(OrderStatus.Ready, order.Status);
    }

    [Fact]
    public async Task DeliverAsync_WhenThereIsNoSuchOrder_RespondsNotFound()
    {
        Guid id = Guid.CreateVersion7();

        IResult result = await WaiterEndpoints.DeliverAsync(id, DeliverHandlerFor(null), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{id}/deliver", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:waiter:order-not-found", response.Text("type"));
    }

    [Fact]
    public async Task DeliverAsync_WhenAnotherScreenSavesFirst_RespondsWithConflict()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Martin, "Martín", Midnight);

        IResult result = await WaiterEndpoints.DeliverAsync(order.Id, DeliverHandlerFor(order, losesTheRace: true), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{order.Id}/deliver", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:changed-meanwhile", response.Text("type"));
    }

    private static DeliverTableOrderHandler DeliverHandlerFor(Order? order, bool losesTheRace = false) =>
        new(new Fake.Orders(order, losesTheRace), new Fake.TheWaiterIs(Martin), TimeProvider.System);

    private static TakeTableOrderHandler HandlerFor(Order? order = null, bool losesTheRace = false) =>
        new(new Fake.Orders(order, losesTheRace), new FakeWaiterQueries(), new Fake.TheWaiterIs(Martin), TimeProvider.System);

    private static class Fake
    {
        public sealed class TheWaiterIs(Guid id) : ICurrentStaffUser
        {
            public string? Username => "martin.p";

            public Guid? Id => id;
        }

        public sealed class Orders(Order? stored, bool losesTheRace) : IOrderRepository
        {
            public Task<Order?> GetForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
                Task.FromResult(stored is not null && stored.Id == orderId ? stored : null);

            public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(losesTheRace ? OrderErrors.ChangedMeanwhile : order);

            public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<Result<Order>> SaveCancellationAsync(Order order, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<Result<Order>> SaveCollectionAsync(Order order, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }
    }
}

internal sealed class FakeWaiterQueries(params WaiterTableOrder[] stored) : IWaiterQueries
{
    public Task<IReadOnlyList<WaiterTableOrder>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WaiterTableOrder>>(stored);

    public Task<string> GetMyNameAsync(CancellationToken cancellationToken) => Task.FromResult("Martín");
}
