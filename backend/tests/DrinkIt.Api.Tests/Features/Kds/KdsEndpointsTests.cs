using DrinkIt.Api.Features.Kds;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Kds;

/// <summary>US-16: taking an order off Nuevos and handing it back, as the bar's tablet calls them.</summary>
public class KdsEndpointsTests
{
    private const string Path = "/kds/orders/K-4821";

    [Fact]
    public async Task StartPreparingAsync_WhenTheOrderIsQueued_RespondsWithNoContent()
    {
        IResult result = await KdsEndpoints.StartPreparingAsync(
            "K-4821",
            new StartPreparingHandler(new Fake.Orders(AQueuedOrder())),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
    }

    // Either no order has that code or it belongs to another venue, and the
    // query filter makes those the same answer on purpose.
    [Fact]
    public async Task StartPreparingAsync_WhenNoOrderHereHasThatCode_RespondsWithNotFound()
    {
        IResult result = await KdsEndpoints.StartPreparingAsync(
            "K-9999",
            new StartPreparingHandler(new Fake.Orders(AQueuedOrder())),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:kds:order-not-found", response.Text("type"));
    }

    [Fact]
    public async Task ReturnToQueueAsync_WhenTheOrderIsInPreparation_RespondsWithNoContent()
    {
        Order order = AQueuedOrder();
        order.StartPreparing();

        IResult result = await KdsEndpoints.ReturnToQueueAsync(
            "K-4821",
            new ReturnToQueueHandler(new Fake.Orders(order)),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReturnToQueueAsync_WhenNoOrderHereHasThatCode_RespondsWithNotFound()
    {
        IResult result = await KdsEndpoints.ReturnToQueueAsync(
            "K-9999",
            new ReturnToQueueHandler(new Fake.Orders()),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:kds:order-not-found", response.Text("type"));
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 2, 0, 0, TimeSpan.Zero);

    // US-18: Listo, Volver a preparación, Entregado and Deshacer answer the
    // same way Preparar does — nothing to hand back, the board reloads.
    [Fact]
    public async Task MarkReadyAsync_WhenInPreparation_RespondsWithNoContent()
    {
        Order order = AQueuedOrder();
        order.StartPreparing();

        IResult result = await KdsEndpoints.MarkReadyAsync(
            "K-4821",
            new MarkReadyHandler(new Fake.Orders(order)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, (await EndpointResponse.Execute(result, Path, HttpMethods.Post)).StatusCode);
        Assert.Equal(OrderStatus.Ready, order.Status);
    }

    [Fact]
    public async Task ReturnToPreparationAsync_WhenReady_RespondsWithNoContent()
    {
        Order order = AReadyOrder();

        IResult result = await KdsEndpoints.ReturnToPreparationAsync(
            "K-4821",
            new ReturnToPreparationHandler(new Fake.Orders(order)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, (await EndpointResponse.Execute(result, Path, HttpMethods.Post)).StatusCode);
        Assert.Equal(OrderStatus.InPreparation, order.Status);
    }

    [Fact]
    public async Task DeliverAsync_WhenReady_RespondsWithNoContent()
    {
        Order order = AReadyOrder();

        IResult result = await KdsEndpoints.DeliverAsync(
            "K-4821",
            new DeliverHandler(new Fake.Orders(order), new FixedClock(Now)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, (await EndpointResponse.Execute(result, Path, HttpMethods.Post)).StatusCode);
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public async Task UndoDeliveryAsync_RightAfterDelivering_RespondsWithNoContent()
    {
        Order order = AReadyOrder();
        order.Deliver(Now);

        IResult result = await KdsEndpoints.UndoDeliveryAsync(
            "K-4821",
            new UndoDeliveryHandler(new Fake.Orders(order), new FixedClock(Now.AddSeconds(5))),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, (await EndpointResponse.Execute(result, Path, HttpMethods.Post)).StatusCode);
        Assert.Equal(OrderStatus.Ready, order.Status);
    }

    [Fact]
    public async Task DeliverAsync_WhenNoOrderHereHasThatCode_RespondsWithNotFound()
    {
        IResult result = await KdsEndpoints.DeliverAsync(
            "K-9999",
            new DeliverHandler(new Fake.Orders(), new FixedClock(Now)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, (await EndpointResponse.Execute(result, Path, HttpMethods.Post)).StatusCode);
    }

    private static Order AReadyOrder()
    {
        Order order = AQueuedOrder();
        order.StartPreparing();
        order.MarkReady();

        return order;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Order AQueuedOrder()
    {
        Order order = Order.Place(
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);
        order.Enqueue();

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

            public Task SaveAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The bar never places an order.");

            public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The bar never places an order.");
        }
    }
}
