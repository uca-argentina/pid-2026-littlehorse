using DrinkIt.Application.Payments;
using MercadoPago.Error;
using MercadoPago.Webhook;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Payments;

/// <summary>
/// Mercado Pago's own validator from its SDK — an HMAC of the notification with
/// the webhook secret — rather than one written here.
/// </summary>
internal sealed class MercadoPagoNotificationVerifier(IOptions<MercadoPagoOptions> options) : IPaymentNotificationVerifier
{
    public bool IsGenuine(string? signature, string? requestId, string? dataId)
    {
        string secret = options.Value.WebhookSecret;

        // Not configured is not "anything goes": nothing is believed.
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature)) return false;

        try
        {
            WebhookSignatureValidator.Validate(signature, requestId, dataId, secret, null, null);

            return true;
        }
        catch (InvalidWebhookSignatureException)
        {
            return false;
        }
    }
}
