using System.Net;
using System.Text;
using System.Text.Json;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Payments;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DrinkIt.Api.IntegrationTests.Payments;

/// <summary>
/// US-24 over HTTP, with Mercado Pago replaced by a double: confirming a
/// digital order hands out where to pay it; the customer's return and the
/// gateway's signed notification move it; and a notification nobody signed
/// moves nothing.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class PaymentsOverHttpTests(SqlServerFixture sql) : IAsyncDisposable
{
    private const string CheckoutUrl = "https://www.mercadopago.com.ar/checkout/v1/redirect?pref_id=123";

    private const int StockBefore = 20;

    private readonly FakeGateway _gateway = new();

    private readonly FakeVerifier _verifier = new();

    private DrinkItApiFactory? _factory;

    private DrinkItApiFactory Factory => _factory ??= new DrinkItApiFactory(sql.ConnectionString, services =>
    {
        services.RemoveAll<IPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(_gateway);
        services.RemoveAll<IPaymentNotificationVerifier>();
        services.AddSingleton<IPaymentNotificationVerifier>(_verifier);
    });

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
    }

    // Confirming no longer pays: the order waits, and the answer says where.
    [Fact]
    public async Task ConfirmOrder_WhenPaidDigitally_AnswersWhereToPayAndLeavesTheOrderWaiting()
    {
        (Venue venue, Product gin) = await ASeededVenue();

        using HttpResponseMessage response = await Confirm(venue, gin);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(CheckoutUrl, body.RootElement.GetProperty("paymentUrl").GetString());
        Assert.Equal("AwaitingPayment", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("paidAt").ValueKind);
    }

    // Nobody can pay, so the drinks go back and the phone is told to try later.
    [Fact]
    public async Task ConfirmOrder_WhenTheGatewayIsDown_AnswersServiceUnavailableAndGivesTheStockBack()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        _gateway.Fails = true;

        using HttpResponseMessage response = await Confirm(venue, gin);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("urn:drinkit:problem:payment:gateway-unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(StockBefore, await StockOf(venue, gin));
    }

    [Fact]
    public async Task ReturnFromPayment_WithoutAPayment_CancelsTheOrderAndGivesItsStockBack()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        (string code, string token) = await AConfirmedOrder(venue, gin);

        using HttpResponseMessage response = await ReturnFromPayment(venue, code, token, paymentId: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Canceled\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(StockBefore, await StockOf(venue, gin));
    }

    [Fact]
    public async Task ReturnFromPayment_WithAnApprovedPaymentOfThisOrder_PaysAndQueuesIt()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        (string code, string token) = await AConfirmedOrder(venue, gin);
        _gateway.Knows(new GatewayPayment("555", await IdOf(venue, code), GatewayPaymentStatus.Approved, DateTimeOffset.UtcNow));

        using HttpResponseMessage response = await ReturnFromPayment(venue, code, token, paymentId: "555");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Queued\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnFromPayment_WithTheWrongToken_AnswersNotFound()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        (string code, _) = await AConfirmedOrder(venue, gin);

        using HttpResponseMessage response = await ReturnFromPayment(venue, code, TrackingToken.New().Value, paymentId: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Notification_WhenSignedAndApproved_PaysAndQueuesTheOrder()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        (string code, _) = await AConfirmedOrder(venue, gin);
        _gateway.Knows(new GatewayPayment("777", await IdOf(venue, code), GatewayPaymentStatus.Approved, DateTimeOffset.UtcNow));

        using HttpResponseMessage response = await Notify(venue, "777");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Queued, await StatusOf(venue, code));
    }

    // Anybody can call this address; only Mercado Pago can sign.
    [Fact]
    public async Task Notification_WhenNotSigned_AnswersUnauthorizedAndMovesNothing()
    {
        (Venue venue, Product gin) = await ASeededVenue();
        (string code, _) = await AConfirmedOrder(venue, gin);
        _gateway.Knows(new GatewayPayment("777", await IdOf(venue, code), GatewayPaymentStatus.Approved, DateTimeOffset.UtcNow));
        _verifier.Genuine = false;

        using HttpResponseMessage response = await Notify(venue, "777");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, await StatusOf(venue, code));
    }

    // The venue comes from the address: another venue's order is out of reach,
    // and the answer is still 200 so Mercado Pago stops retrying it.
    [Fact]
    public async Task Notification_ThroughAnotherVenuesAddress_MovesNothing()
    {
        (Venue mine, _) = await ASeededVenue();
        (Venue theirs, Product theirGin) = await ASeededVenue();
        (string code, _) = await AConfirmedOrder(theirs, theirGin);
        _gateway.Knows(new GatewayPayment("777", await IdOf(theirs, code), GatewayPaymentStatus.Approved, DateTimeOffset.UtcNow));

        using HttpResponseMessage response = await Notify(mine, "777");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, await StatusOf(theirs, code));
    }

    // Mercado Pago also notifies about things that are not payments.
    [Fact]
    public async Task Notification_WhenItIsNotAboutAPayment_IsAcknowledgedAndIgnored()
    {
        (Venue venue, _) = await ASeededVenue();

        using HttpResponseMessage response = await Notify(venue, "12345", type: "merchant_order");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> Confirm(Venue venue, Product gin)
    {
        using HttpClient client = Factory.CreateClient();
        string body = $$$"""
            {"customerName":"María Quadro","method":"Digital","idempotencyKey":"{{{Guid.NewGuid()}}}",
             "lines":[{"productId":"{{{gin.Id}}}","quantity":1,"note":null}]}
            """;

        return await client.PostAsync($"/{venue.Slug}/orders", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private async Task<(string Code, string Token)> AConfirmedOrder(Venue venue, Product gin)
    {
        using HttpResponseMessage response = await Confirm(venue, gin);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (body.RootElement.GetProperty("code").GetString()!, body.RootElement.GetProperty("trackingToken").GetString()!);
    }

    private async Task<HttpResponseMessage> ReturnFromPayment(Venue venue, string code, string token, string? paymentId)
    {
        using HttpClient client = Factory.CreateClient();
        string body = paymentId is null ? "{}" : $$"""{"paymentId":"{{paymentId}}"}""";

        return await client.PostAsync(
            $"/{venue.Slug}/orders/{code}/{token}/payment",
            new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private async Task<HttpResponseMessage> Notify(Venue venue, string dataId, string type = "payment")
    {
        using HttpClient client = Factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, $"/{venue.Slug}/payments/notifications?data.id={dataId}&type={type}");
        request.Headers.Add("x-signature", "ts=1,v1=abc");
        request.Headers.Add("x-request-id", "bb56a2f1");
        request.Content = new StringContent($$$"""{"type":"{{{type}}}","data":{"id":"{{{dataId}}}"}}""", Encoding.UTF8, "application/json");

        return await client.SendAsync(request);
    }

    private async Task<Guid> IdOf(Venue venue, string code)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return (await check.Orders.SingleAsync(row => row.Code == OrderCode.Parse(code))).Id;
    }

    private async Task<OrderStatus> StatusOf(Venue venue, string code)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return (await check.Orders.SingleAsync(row => row.Code == OrderCode.Parse(code))).Status;
    }

    private async Task<int> StockOf(Venue venue, Product product)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return (await check.Products.SingleAsync(row => row.Id == product.Id)).Stock;
    }

    private async Task<(Venue Venue, Product Gin)> ASeededVenue()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, StockBefore, category.Id);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        await seed.SaveChangesAsync();

        return (venue, gin);
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        private readonly Dictionary<string, GatewayPayment> _payments = [];

        public bool Fails { get; set; }

        public void Knows(GatewayPayment payment) => _payments[payment.Id] = payment;

        public Task<Result<string>> StartCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<Result<string>>(Fails ? PaymentErrors.GatewayUnavailable : CheckoutUrl);

        public Task<GatewayPayment?> FindPaymentAsync(string paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(_payments.GetValueOrDefault(paymentId));
    }

    private sealed class FakeVerifier : IPaymentNotificationVerifier
    {
        public bool Genuine { get; set; } = true;

        public bool IsGenuine(string? signature, string? requestId, string? dataId) => Genuine;
    }
}
