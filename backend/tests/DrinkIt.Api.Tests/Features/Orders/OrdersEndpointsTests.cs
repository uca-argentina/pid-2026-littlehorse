using DrinkIt.Api.Features.Orders;
using DrinkIt.Api.Tenancy;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Venues;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Orders;

/// <summary>
/// US-11. The second endpoint that answers without a token, and the first one
/// that writes: the shape below is the contract a stranger's phone gets, and
/// the Angular client is generated from it.
/// </summary>
public class OrdersEndpointsTests
{
    private const string Path = "/bar-alfa/orders";

    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private readonly Product _gin = Product.Create(TheVenue, "Gin Tonic", null, null, 4500m, 20);

    [Fact]
    public async Task ConfirmAsync_WhenTheOrderIsGood_RespondsWithItsCode()
    {
        HttpResponseSnapshot response = await Confirm(ARequestFor(2));

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("A-0000", response.Text("code"));
        Assert.Equal("María Quadro", response.Text("customerName"));
        Assert.Equal(9000m, response.Body.GetProperty("total").GetDecimal());
        Assert.Equal("Queued", response.Text("status"));
    }

    // 201 carries where the order can be read back, which US-12 will answer.
    [Fact]
    public async Task ConfirmAsync_WhenTheOrderIsGood_PointsAtWhereItCanBeFollowed()
    {
        HttpResponseSnapshot response = await Confirm(ARequestFor(1));

        Assert.Equal("/bar-alfa/orders/A-0000", response.Location);
    }

    // Criterion 3, as the phone receives it: a 400 whose type the screen can
    // branch on, and a detail it can show as it is.
    [Fact]
    public async Task ConfirmAsync_WhenThereIsNoSurname_RespondsWithBadRequest()
    {
        HttpResponseSnapshot response = await Confirm(ARequestFor(1) with { CustomerName = "Euge" });

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:order:name-needs-surname", response.Text("type"));
    }

    /// <summary>
    /// A drink that ran out is a conflict and not a bad request: what the phone
    /// sent was right when it was sent, and what changed is the world. The
    /// screen needs to tell those apart to say "se agotó" instead of "revisá
    /// lo que escribiste".
    /// </summary>
    [Fact]
    public async Task ConfirmAsync_WhenADrinkRanOut_RespondsWithConflict()
    {
        _gin.Take(19);

        HttpResponseSnapshot response = await Confirm(ARequestFor(2));

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:sold-out", response.Text("type"));
        Assert.Contains("Gin Tonic", response.Text("detail"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmAsync_WhenNoVenueHasThatSlug_RespondsWithNotFound()
    {
        IResult result = await OrdersEndpoints.ConfirmAsync(
            "bar-que-no-existe", ARequestFor(1), new CurrentVenue(), AHandler(), CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:venue:not-found", response.Text("type"));
    }

    // A body with no lines at all, which is what a hand-rolled request looks
    // like. It is answered, not thrown.
    [Fact]
    public async Task ConfirmAsync_WhenTheBodyCarriesNoLines_RespondsWithBadRequest()
    {
        HttpResponseSnapshot response = await Confirm(
            new ConfirmOrderRequest("María Quadro", PaymentMethod.Digital, "abc-123", null));

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:empty", response.Text("type"));
    }

    // The two the screen draws switched off. Asking the API for them directly
    // is answered rather than half-done.
    [Fact]
    public async Task ConfirmAsync_WhenPayingWithCash_RespondsWithBadRequest()
    {
        HttpResponseSnapshot response = await Confirm(ARequestFor(1) with { Method = PaymentMethod.Cash });

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:order:payment-method-unavailable", response.Text("type"));
    }

    // Nothing about how many are left leaves the building, here either.
    [Fact]
    public async Task ConfirmAsync_WhenTheOrderIsGood_SendsNoStockCount()
    {
        HttpResponseSnapshot response = await Confirm(ARequestFor(1));

        Assert.DoesNotContain("stock", response.Raw, StringComparison.OrdinalIgnoreCase);
    }

    private ConfirmOrderRequest ARequestFor(int quantity) => new(
        "María Quadro",
        PaymentMethod.Digital,
        "abc-123",
        [new OrderLineRequestBody(_gin.Id, quantity, null)]);

    private async Task<HttpResponseSnapshot> Confirm(ConfirmOrderRequest request)
    {
        CurrentVenue venue = new();
        venue.Resolve(new VenueIdentity(TheVenue, "Bar Alfa", "bar-alfa"));

        IResult result = await OrdersEndpoints.ConfirmAsync(
            "bar-alfa", request, venue, AHandler(), CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Post);
    }

    private ConfirmOrderHandler AHandler() => new(
        new Fake.Orders(),
        new Fake.Menu(_gin),
        new Fake.Sequence(),
        [new DigitalPaymentStrategy(TimeProvider.System)],
        new Fake.Venue());

    private static class Fake
    {
        public sealed class Venue : Application.Common.ICurrentVenue
        {
            public Guid Id => TheVenue;
        }

        public sealed class Sequence : IOrderCodeSequence
        {
            public Task<OrderCode> NextAsync(CancellationToken cancellationToken) =>
                Task.FromResult(OrderCode.First);
        }

        public sealed class Orders : IOrderRepository
        {
            public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
                Task.FromResult<Order?>(null);

            public Task AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
                Task.CompletedTask;
        }

        public sealed class Menu(params Product[] products) : IProductsForOrdering
        {
            public Task<IReadOnlyList<Product>> GetForOrderingAsync(
                IReadOnlyCollection<Guid> ids,
                CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<Product>>(
                    [.. products.Where(product => ids.Contains(product.Id))]);

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
