using System.Globalization;
using DrinkIt.Application.Payments;
using DrinkIt.Domain.Orders;
using MercadoPago.Client.Preference;
using MercadoPago.Resource.Payment;

namespace DrinkIt.Infrastructure.Payments;

/// <summary>
/// What goes to Mercado Pago and how its answers are read — the rules of the
/// adapter, apart from the call so they can be tested without the network.
/// </summary>
internal static class MercadoPagoMapping
{
    /// <summary>Argentina (MLA): the venue charges in pesos.</summary>
    private const string Currency = "ARS";

    /// <summary>What the customer reads on their card statement.</summary>
    private const string StatementDescriptor = "DRINKIT";

    /// <summary>
    /// Paid hours later, at a Rapipago or an ATM: never within the fifteen
    /// minutes an order waits, so not offered (/mp-review, 2026-09-30).
    /// </summary>
    private static readonly string[] SlowPaymentTypes = ["ticket", "atm"];

    public static PreferenceRequest PreferenceFor(PaymentCheckoutRequest request, MercadoPagoOptions options)
    {
        Order order = request.Order;
        string back = $"{options.PublicAppUrl.TrimEnd('/')}/{request.VenueSlug}/orders/{order.Code.Value}/{order.TrackingToken.Value}/payment";

        return new PreferenceRequest
        {
            Items =
            [
                .. order.Items.Select(item => new PreferenceItemRequest
                {
                    Id = item.ProductId.ToString(),
                    Title = item.ProductName,
                    Description = item.Note is null ? item.ProductName : $"{item.ProductName} ({item.Note})",
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    CurrencyId = Currency,
                }),
            ],
            // The checkout asks for a full name; Mercado Pago approves more
            // payments when it knows who is paying.
            Payer = PayerFor(order.CustomerName),
            StatementDescriptor = StatementDescriptor,
            // Approved or rejected, never pending: with fifteen minutes to pay,
            // a payment left under review could not finish in time.
            BinaryMode = true,
            PaymentMethods = new PreferencePaymentMethodsRequest
            {
                ExcludedPaymentTypes = [.. SlowPaymentTypes.Select(type => new PreferencePaymentTypeRequest { Id = type })],
                // One drink, one payment.
                Installments = 1,
            },
            // The reconciliation anchor: every payment comes back naming it.
            ExternalReference = order.Id.ToString(),
            // One return screen, whatever happened: it asks the API what did.
            BackUrls = new PreferenceBackUrlsRequest { Success = back, Failure = back, Pending = back },
            // Mercado Pago refuses auto_return with a localhost return address.
            AutoReturn = IsPublic(options.PublicAppUrl) ? "approved" : null,
            NotificationUrl = IsPublic(options.PublicApiUrl)
                ? $"{options.PublicApiUrl.TrimEnd('/')}/{request.VenueSlug}/payments/notifications"
                : null,
            // After this Mercado Pago refuses the payment, so nothing can be
            // paid for an order the system has already canceled.
            Expires = true,
            ExpirationDateTo = request.ExpiresAt.UtcDateTime,
        };
    }

    /// <summary>The first word is the name; the rest, however many words, the surname.</summary>
    private static PreferencePayerRequest PayerFor(string customerName)
    {
        string[] words = customerName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new PreferencePayerRequest { Name = words[0], Surname = words.Length > 1 ? words[1] : null };
    }

    /// <summary>
    /// A payment as the order reads it, or null when it is not one of ours —
    /// no reference, or a reference that is not an order id.
    /// </summary>
    public static GatewayPayment? PaymentFrom(Payment payment)
    {
        if (!Guid.TryParse(payment.ExternalReference, out Guid orderId)) return null;

        return new GatewayPayment(
            Convert.ToString(payment.Id, CultureInfo.InvariantCulture) ?? string.Empty,
            orderId,
            StatusOf(payment.Status),
            payment.DateApproved is DateTime approved ? new DateTimeOffset(DateTime.SpecifyKind(approved, DateTimeKind.Utc)) : null);
    }

    /// <summary>
    /// Mercado Pago's statuses, as far as an order cares: the money came in, it
    /// will not, or it is not decided yet. Anything unknown is "not decided":
    /// waiting is safe, canceling or paying on a guess is not.
    /// </summary>
    private static GatewayPaymentStatus StatusOf(string? status) => status switch
    {
        "approved" => GatewayPaymentStatus.Approved,
        "rejected" or "cancelled" => GatewayPaymentStatus.Rejected,
        _ => GatewayPaymentStatus.Pending,
    };

    /// <summary>Https, and not this machine: what Mercado Pago accepts and can reach.</summary>
    public static bool IsPublic(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !uri.IsLoopback
        && uri.Host != "0.0.0.0";
}
