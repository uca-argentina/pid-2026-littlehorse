using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Payments;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Payments;

/// <summary>
/// US-24, paid through Mercado Pago's Checkout Pro: confirming no longer pays.
/// The order waits, and the customer is sent to the gateway's page to pay it.
/// </summary>
public class DigitalPaymentStrategyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 1, 0, 0, TimeSpan.Zero);

    private const string CheckoutUrl = "https://www.mercadopago.com.ar/checkout/v1/redirect?pref_id=123";

    private static Order ACart() =>
        Order.Place(
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 2, null)]);

    [Fact]
    public void Settle_Always_LeavesTheOrderWaitingForItsPayment()
    {
        Order order = ACart();

        new DigitalPaymentStrategy(new GatewayDouble(), new FixedClock(Now)).Settle(order);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.PaidAt);
    }

    [Fact]
    public async Task HandOffAsync_WhenTheGatewayOpensACheckout_KeepsItsUrlAndSendsTheCustomerThere()
    {
        Order order = ACart();
        GatewayDouble gateway = new() { Opens = CheckoutUrl };
        DigitalPaymentStrategy strategy = new(gateway, new FixedClock(Now));
        strategy.Settle(order);

        Result<string?> result = await strategy.HandOffAsync(order, "bar-alfa", CancellationToken.None);

        Assert.Equal(CheckoutUrl, result.Value);
        Assert.Equal(CheckoutUrl, order.PaymentUrl);
        Assert.Same(order, gateway.Asked!.Order);
        Assert.Equal("bar-alfa", gateway.Asked.VenueSlug);
    }

    // Decided on 2026-09-30: nobody waits longer than this for a drink they
    // have not paid for, and the gateway refuses the payment after it.
    [Fact]
    public async Task HandOffAsync_Always_AsksForACheckoutThatExpiresInFifteenMinutes()
    {
        Order order = ACart();
        GatewayDouble gateway = new() { Opens = CheckoutUrl };
        DigitalPaymentStrategy strategy = new(gateway, new FixedClock(Now));
        strategy.Settle(order);

        await strategy.HandOffAsync(order, "bar-alfa", CancellationToken.None);

        Assert.Equal(Now + DigitalPaymentStrategy.PaymentWindow, gateway.Asked!.ExpiresAt);
        Assert.Equal(TimeSpan.FromMinutes(15), DigitalPaymentStrategy.PaymentWindow);
    }

    [Fact]
    public async Task HandOffAsync_WhenTheGatewayFails_SaysSoAndKeepsNoUrl()
    {
        Order order = ACart();
        DigitalPaymentStrategy strategy = new(new GatewayDouble { Fails = true }, new FixedClock(Now));
        strategy.Settle(order);

        Result<string?> result = await strategy.HandOffAsync(order, "bar-alfa", CancellationToken.None);

        Assert.Equal(PaymentErrors.GatewayUnavailable, result.Error);
        Assert.Null(order.PaymentUrl);
    }

    private sealed class GatewayDouble : IPaymentGateway
    {
        public string Opens { get; init; } = CheckoutUrl;

        public bool Fails { get; init; }

        public PaymentCheckoutRequest? Asked { get; private set; }

        public Task<Result<string>> StartCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken)
        {
            Asked = request;

            return Task.FromResult<Result<string>>(Fails ? PaymentErrors.GatewayUnavailable : Opens);
        }

        public Task<GatewayPayment?> FindPaymentAsync(string paymentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Handing off never looks a payment up.");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
