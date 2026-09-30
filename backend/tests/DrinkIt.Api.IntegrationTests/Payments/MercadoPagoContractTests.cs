using DrinkIt.Application.Common;
using DrinkIt.Application.Payments;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Payments;
using MercadoPago.Client;
using MercadoPago.Client.Preference;
using MercadoPago.Resource.Preference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrinkIt.Api.IntegrationTests.Payments;

/// <summary>
/// US-24: the Checkout Pro adapter against Mercado Pago's real API, with the
/// test access token — the contract every other test assumes. Nothing here is
/// charged: the token is a test seller's, and production is not activated.
/// </summary>
/// <remarks>
/// Skipped where there is no token, so a machine without one, and the CI, are
/// not blocked by a service they cannot reach. It is read like the API reads
/// it: "dotnet user-secrets" of DrinkIt.Api, or MercadoPago__AccessToken.
/// </remarks>
public sealed class MercadoPagoContractTests
{
    private static readonly MercadoPagoOptions Options = new()
    {
        AccessToken = MercadoPagoTestToken.Value ?? string.Empty,
        // Public and HTTPS, like the deployed PWA: Mercado Pago drops a
        // localhost return address (see the last test).
        PublicAppUrl = "https://drinkit.example.com",
    };

    private readonly MercadoPagoGateway _gateway = new(
        Microsoft.Extensions.Options.Options.Create(Options),
        NullLogger<MercadoPagoGateway>.Instance);

    private static Order AnOrderAwaitingPayment()
    {
        Order order = Order.Place(
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [
                new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 2, null),
                new NewOrderItem(Guid.CreateVersion7(), "Fernet", 3800m, 1, null),
            ]);
        order.AwaitPayment(PaymentMethod.Digital);

        return order;
    }

    private static PaymentCheckoutRequest ACheckoutFor(Order order) =>
        new(order, "bar-alfa", DateTimeOffset.UtcNow.AddMinutes(15));

    [MercadoPagoFact]
    public async Task StartCheckoutAsync_WithTheTestToken_OpensAMercadoPagoCheckout()
    {
        Result<string> checkout = await _gateway.StartCheckoutAsync(ACheckoutFor(AnOrderAwaitingPayment()), CancellationToken.None);

        Assert.True(checkout.IsSuccess, checkout.Error?.Message);
        Assert.StartsWith("https://www.mercadopago.com.ar/checkout/", checkout.Value, StringComparison.Ordinal);
    }

    // Read back from Mercado Pago: what it stored is what the order said.
    [MercadoPagoFact]
    public async Task StartCheckoutAsync_WithTheTestToken_StoresTheOrdersLinesReferenceAndExpiry()
    {
        Order order = AnOrderAwaitingPayment();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);

        Result<string> checkout = await _gateway.StartCheckoutAsync(new PaymentCheckoutRequest(order, "bar-alfa", expiresAt), CancellationToken.None);
        Preference stored = await new PreferenceClient().GetAsync(PreferenceIdIn(checkout.Value), TestToken(), CancellationToken.None);

        Assert.Equal(order.Id.ToString(), stored.ExternalReference);
        Assert.Equal(["Gin Tonic", "Fernet"], stored.Items.Select(item => item.Title));
        Assert.All(stored.Items, item => Assert.Equal("ARS", item.CurrencyId));
        Assert.True(stored.Expires);
        Assert.Equal(expiresAt.UtcDateTime, stored.ExpirationDateTo!.Value.ToUniversalTime(), TimeSpan.FromSeconds(1));
        Assert.EndsWith($"/bar-alfa/orders/K-4821/{order.TrackingToken.Value}/payment", stored.BackUrls.Success, StringComparison.Ordinal);
    }

    [MercadoPagoFact]
    public async Task FindPaymentAsync_WithAnIdMercadoPagoDoesNotKnow_FindsNothing()
    {
        Assert.Null(await _gateway.FindPaymentAsync("1", CancellationToken.None));
    }

    /// <summary>
    /// Found out here on 2026-09-30: Mercado Pago does not deduplicate
    /// preferences — the same order asked for twice gets two. Which is why the
    /// order keeps its PaymentUrl and a retry of the confirmation answers that
    /// one (ConfirmOrderHandler), rather than trusting the gateway to.
    /// </summary>
    [MercadoPagoFact]
    public async Task StartCheckoutAsync_WhenTheSameOrderIsAskedTwice_OpensTwoCheckouts()
    {
        PaymentCheckoutRequest request = ACheckoutFor(AnOrderAwaitingPayment());

        Result<string> first = await _gateway.StartCheckoutAsync(request, CancellationToken.None);
        Result<string> second = await _gateway.StartCheckoutAsync(request, CancellationToken.None);

        Assert.NotEqual(first.Value, second.Value);
    }

    /// <summary>
    /// Found out here on 2026-09-30: a return address on localhost is dropped
    /// without a word, so after paying on a laptop nobody is sent back. Local
    /// testing goes through an HTTPS tunnel (docs/pagos-mercado-pago.md).
    /// </summary>
    [MercadoPagoFact]
    public async Task StartCheckoutAsync_WithALocalReturnAddress_MercadoPagoDropsIt()
    {
        MercadoPagoGateway local = new(
            Microsoft.Extensions.Options.Options.Create(new MercadoPagoOptions
            {
                AccessToken = Options.AccessToken,
                PublicAppUrl = "http://localhost:4200",
            }),
            NullLogger<MercadoPagoGateway>.Instance);

        Result<string> checkout = await local.StartCheckoutAsync(ACheckoutFor(AnOrderAwaitingPayment()), CancellationToken.None);
        Preference stored = await new PreferenceClient().GetAsync(PreferenceIdIn(checkout.Value), TestToken(), CancellationToken.None);

        Assert.True(string.IsNullOrEmpty(stored.BackUrls.Success));
    }

    private static string PreferenceIdIn(string checkoutUrl) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(checkoutUrl).Query)["pref_id"]!;

    private static RequestOptions TestToken() => new() { AccessToken = Options.AccessToken };
}

/// <summary>A test that talks to Mercado Pago, skipped where there is no test token to do it with.</summary>
internal sealed class MercadoPagoFactAttribute : FactAttribute
{
    public MercadoPagoFactAttribute()
    {
        if (MercadoPagoTestToken.Value is null)
            Skip = "No Mercado Pago test token: set it with dotnet user-secrets on DrinkIt.Api, or MercadoPago__AccessToken.";
    }
}

/// <summary>The test access token, read the way the API reads it in Development.</summary>
internal static class MercadoPagoTestToken
{
    public static readonly string? Value = Read();

    private static string? Read()
    {
        string? token = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build()["MercadoPago:AccessToken"];

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
