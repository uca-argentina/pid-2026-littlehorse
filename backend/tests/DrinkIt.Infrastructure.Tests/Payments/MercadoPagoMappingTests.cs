using DrinkIt.Application.Payments;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Payments;
using MercadoPago.Client.Preference;
using Payment = MercadoPago.Resource.Payment.Payment;

namespace DrinkIt.Infrastructure.Tests.Payments;

/// <summary>
/// US-24: what the Checkout Pro adapter sends to Mercado Pago and how it reads
/// back what Mercado Pago says. The SDK call itself is one line; the rules are
/// here, and they are tested without the network.
/// </summary>
public class MercadoPagoMappingTests
{
    private static readonly DateTimeOffset ExpiresAt = new(2026, 9, 30, 1, 15, 0, TimeSpan.Zero);

    private static readonly MercadoPagoOptions Local = new()
    {
        AccessToken = "APP_USR-test",
        PublicAppUrl = "http://localhost:4200",
        PublicApiUrl = "http://localhost:5127",
    };

    private static readonly MercadoPagoOptions Deployed = new()
    {
        AccessToken = "APP_USR-test",
        PublicAppUrl = "https://drinkit.example.com",
        PublicApiUrl = "https://api.drinkit.example.com",
    };

    private static Order AnOrderAwaitingPayment()
    {
        Order order = Order.Place(
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [
                new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 2, null),
                new NewOrderItem(Guid.CreateVersion7(), "Fernet", 3800m, 1, "sin hielo"),
            ]);
        order.AwaitPayment(PaymentMethod.Digital);

        return order;
    }

    private static PreferenceRequest PreferenceFor(Order order, MercadoPagoOptions options) =>
        MercadoPagoMapping.PreferenceFor(new PaymentCheckoutRequest(order, "bar-alfa", ExpiresAt), options);

    [Fact]
    public void PreferenceFor_Always_ChargesEveryLineInPesos()
    {
        PreferenceRequest preference = PreferenceFor(AnOrderAwaitingPayment(), Local);

        Assert.Collection(
            preference.Items,
            gin =>
            {
                Assert.Equal("Gin Tonic", gin.Title);
                Assert.Equal(2, gin.Quantity);
                Assert.Equal(4500m, gin.UnitPrice);
                Assert.Equal("ARS", gin.CurrencyId);
            },
            fernet => Assert.Equal("Fernet", fernet.Title));
    }

    // The reconciliation anchor: whatever comes back names the order by this.
    [Fact]
    public void PreferenceFor_Always_NamesTheOrderAsItsExternalReference()
    {
        Order order = AnOrderAwaitingPayment();

        Assert.Equal(order.Id.ToString(), PreferenceFor(order, Local).ExternalReference);
    }

    [Fact]
    public void PreferenceFor_Always_StopsTakingPaymentsWhenTheOrderExpires()
    {
        PreferenceRequest preference = PreferenceFor(AnOrderAwaitingPayment(), Local);

        Assert.True(preference.Expires);
        Assert.Equal(ExpiresAt.UtcDateTime, preference.ExpirationDateTo!.Value.ToUniversalTime());
    }

    // Back to the PWA's own return screen for this order, whichever way it went.
    [Fact]
    public void PreferenceFor_Always_SendsTheCustomerBackToTheirOrdersReturnScreen()
    {
        Order order = AnOrderAwaitingPayment();

        PreferenceRequest preference = PreferenceFor(order, Local);

        string back = $"http://localhost:4200/bar-alfa/orders/K-4821/{order.TrackingToken.Value}/payment";
        Assert.Equal(back, preference.BackUrls.Success);
        Assert.Equal(back, preference.BackUrls.Failure);
        Assert.Equal(back, preference.BackUrls.Pending);
    }

    // Mercado Pago refuses auto_return with a localhost return address.
    [Fact]
    public void PreferenceFor_WhenTheAppIsLocal_DoesNotAskToReturnOnItsOwn()
    {
        Assert.Null(PreferenceFor(AnOrderAwaitingPayment(), Local).AutoReturn);
    }

    [Fact]
    public void PreferenceFor_WhenTheAppIsPublic_ReturnsOnItsOwnOnceApproved()
    {
        Assert.Equal("approved", PreferenceFor(AnOrderAwaitingPayment(), Deployed).AutoReturn);
    }

    // The notification carries no venue: the slug in its address is how it
    // arrives scoped, like every other customer route (decided 2026-09-30).
    [Fact]
    public void PreferenceFor_WhenTheApiIsPublic_NotifiesThroughThisVenuesAddress()
    {
        Assert.Equal(
            "https://api.drinkit.example.com/bar-alfa/payments/notifications",
            PreferenceFor(AnOrderAwaitingPayment(), Deployed).NotificationUrl);
    }

    // Mercado Pago cannot reach a laptop: no address rather than a dead one.
    [Fact]
    public void PreferenceFor_WhenTheApiIsLocal_AsksForNoNotification()
    {
        Assert.Null(PreferenceFor(AnOrderAwaitingPayment(), Local).NotificationUrl);
    }

    /// <summary>
    /// /mp-review, 2026-09-30: only "approved" or "rejected", never "pending".
    /// With fifteen minutes to pay, a payment left under review cannot finish
    /// in time — the order would be canceled while it waits.
    /// </summary>
    [Fact]
    public void PreferenceFor_Always_AsksForAnAnswerOnTheSpot()
    {
        Assert.True(PreferenceFor(AnOrderAwaitingPayment(), Local).BinaryMode);
    }

    // Cash at a Rapipago or an ATM transfer is paid hours later: never in fifteen minutes.
    [Fact]
    public void PreferenceFor_Always_LeavesOutWaysOfPayingThatTakeHours()
    {
        PreferenceRequest preference = PreferenceFor(AnOrderAwaitingPayment(), Local);

        Assert.Equal(["ticket", "atm"], preference.PaymentMethods.ExcludedPaymentTypes.Select(type => type.Id));
    }

    [Fact]
    public void PreferenceFor_Always_ChargesADrinkInASingleInstallment()
    {
        Assert.Equal(1, PreferenceFor(AnOrderAwaitingPayment(), Local).PaymentMethods.Installments);
    }

    // The checkout asks for a full name, so Mercado Pago can have it split.
    [Fact]
    public void PreferenceFor_Always_NamesThePayer()
    {
        PreferenceRequest preference = PreferenceFor(AnOrderAwaitingPayment(), Local);

        Assert.Equal("María", preference.Payer.Name);
        Assert.Equal("Quadro", preference.Payer.Surname);
    }

    [Fact]
    public void PreferenceFor_WhenTheSurnameHasMoreThanOneWord_KeepsItWhole()
    {
        Order order = Order.Place(
            Guid.CreateVersion7(),
            "Juan Martín del Potro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.AwaitPayment(PaymentMethod.Digital);

        PreferenceRequest preference = PreferenceFor(order, Local);

        Assert.Equal("Juan", preference.Payer.Name);
        Assert.Equal("Martín del Potro", preference.Payer.Surname);
    }

    // What the customer reads on their card statement.
    [Fact]
    public void PreferenceFor_Always_SignsTheChargeAsDrinkIt()
    {
        Assert.Equal("DRINKIT", PreferenceFor(AnOrderAwaitingPayment(), Local).StatementDescriptor);
    }

    [Fact]
    public void PreferenceFor_Always_IdentifiesAndDescribesEachProduct()
    {
        Order order = AnOrderAwaitingPayment();

        PreferenceItemRequest gin = PreferenceFor(order, Local).Items[0];

        Assert.Equal(order.Items[0].ProductId.ToString(), gin.Id);
        Assert.Equal("Gin Tonic", gin.Description);
    }

    // The customer's note travels with the drink it belongs to.
    [Fact]
    public void PreferenceFor_WhenALineHasANote_DescribesItWithTheNote()
    {
        Assert.Equal("Fernet (sin hielo)", PreferenceFor(AnOrderAwaitingPayment(), Local).Items[1].Description);
    }

    [Theory]
    [InlineData("approved", GatewayPaymentStatus.Approved)]
    [InlineData("pending", GatewayPaymentStatus.Pending)]
    [InlineData("in_process", GatewayPaymentStatus.Pending)]
    [InlineData("authorized", GatewayPaymentStatus.Pending)]
    [InlineData("in_mediation", GatewayPaymentStatus.Pending)]
    [InlineData("rejected", GatewayPaymentStatus.Rejected)]
    [InlineData("cancelled", GatewayPaymentStatus.Rejected)]
    public void PaymentFrom_Always_ReadsMercadoPagosStatusAsTheOrderNeedsIt(string status, GatewayPaymentStatus expected)
    {
        Guid orderId = Guid.CreateVersion7();

        GatewayPayment? payment = MercadoPagoMapping.PaymentFrom(
            new Payment { Id = 123, Status = status, ExternalReference = orderId.ToString() });

        Assert.Equal(expected, payment!.Status);
        Assert.Equal(orderId, payment.OrderId);
        Assert.Equal("123", payment.Id);
    }

    [Fact]
    public void PaymentFrom_WhenApproved_KeepsTheMomentItWasApproved()
    {
        DateTime approved = new(2026, 9, 30, 1, 5, 0, DateTimeKind.Utc);

        GatewayPayment? payment = MercadoPagoMapping.PaymentFrom(
            new Payment { Id = 123, Status = "approved", ExternalReference = Guid.CreateVersion7().ToString(), DateApproved = approved });

        Assert.Equal(new DateTimeOffset(approved), payment!.ApprovedAt);
    }

    // A payment that is not one of ours — no reference, or not an order id —
    // is nothing this system can move.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("order-123")]
    public void PaymentFrom_WhenTheReferenceIsNotAnOrderId_ReadsNothing(string? reference)
    {
        Assert.Null(MercadoPagoMapping.PaymentFrom(new Payment { Id = 123, Status = "approved", ExternalReference = reference }));
    }
}
