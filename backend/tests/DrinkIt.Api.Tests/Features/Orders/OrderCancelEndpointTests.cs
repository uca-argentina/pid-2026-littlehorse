using DrinkIt.Api.Features.Orders;
using DrinkIt.Api.Tenancy;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Venues;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Orders;

/// <summary>
/// US-23: the customer cancels from their own tracking link, before paying at
/// the till. Guarded by the same token as that link, and answering the same
/// 404 to every way of not getting in.
/// </summary>
public class OrderCancelEndpointTests
{
    [Fact]
    public async Task CancelAsync_WhenTheOrderAwaitsPayment_RespondsWithNoContent()
    {
        Order order = AnOrderWaitingForCash();

        HttpResponseSnapshot response = await Cancel(order, order.TrackingToken.Value);

        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
        Assert.Equal(OrderStatus.Canceled, order.Status);
    }

    [Fact]
    public async Task CancelAsync_WhenTheTokenIsNotTheOrders_RespondsWithNotFound()
    {
        Order order = AnOrderWaitingForCash();

        HttpResponseSnapshot response = await Cancel(order, TrackingToken.New().Value);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:not-found", response.Text("type"));
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public async Task CancelAsync_WhenNoVenueHasThatSlug_RespondsWithNotFound()
    {
        Order order = AnOrderWaitingForCash();

        IResult result = await OrderTrackingEndpoint.CancelAsync(
            "bar-que-no-existe", "K-4821", order.TrackingToken.Value, new CurrentVenue(),
            new CancelOrderHandler(new Fake.Orders(order)), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, PathOf(order), HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    // Paid is the bar's: a conflict the screen can name, not an error page.
    [Fact]
    public async Task CancelAsync_WhenTheOrderWasPaid_RespondsWithConflict()
    {
        Order order = AnOrderWaitingForCash();
        order.CollectCash(DateTimeOffset.UtcNow, "laura.caja");

        HttpResponseSnapshot response = await Cancel(order, order.TrackingToken.Value);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:not-cancelable", response.Text("type"));
    }

    private static Order AnOrderWaitingForCash()
    {
        Order order = Order.Place(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.AwaitPayment(PaymentMethod.Cash);

        return order;
    }

    private static string PathOf(Order order) => $"/bar-alfa/orders/K-4821/{order.TrackingToken.Value}/cancel";

    private static async Task<HttpResponseSnapshot> Cancel(Order order, string token)
    {
        CurrentVenue venue = new();
        venue.Resolve(new VenueIdentity(Guid.CreateVersion7(), "Bar Alfa", "bar-alfa"));

        IResult result = await OrderTrackingEndpoint.CancelAsync(
            "bar-alfa", "K-4821", token, venue, new CancelOrderHandler(new Fake.Orders(order)), CancellationToken.None);

        return await EndpointResponse.Execute(result, PathOf(order), HttpMethods.Post);
    }

    private static class Fake
    {
        public sealed class Orders(Order stored) : IOrderRepository
        {
            public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
                Task.FromResult(stored.Code == code ? stored : null);

            public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
                Task.FromResult(stored.TrackingToken == token ? stored : null);

            public Task<Result<Order>> SaveCancellationAsync(Order order, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Order>>(order);

            public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken) =>
                throw new NotSupportedException("Canceling saves through SaveCancellationAsync.");

            public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The customer does not place an order here.");

            public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The customer does not place an order here.");
        }
    }
}
