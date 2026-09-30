using DrinkIt.Application.Common;
using DrinkIt.Application.Payments;
using DrinkIt.Domain.Orders;
using MercadoPago.Client;
using MercadoPago.Client.Payment;
using MercadoPago.Client.Preference;
using MercadoPago.Error;
using MercadoPago.Resource.Payment;
using MercadoPago.Resource.Preference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Payments;

/// <summary>
/// Checkout Pro through Mercado Pago's official SDK (US-24). The rules live in
/// <see cref="MercadoPagoMapping"/>; this is only the call.
/// </summary>
/// <remarks>
/// The access token goes with each request rather than into the SDK's static
/// MercadoPagoConfig: a global the whole process shares is one no test can
/// set safely and no second configuration can ever use.
/// </remarks>
internal sealed partial class MercadoPagoGateway(
    IOptions<MercadoPagoOptions> options,
    ILogger<MercadoPagoGateway> logger) : IPaymentGateway
{
    public async Task<Result<PaymentCheckout>> StartCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // No idempotency key: Mercado Pago ignores it on preferences and
            // opens a new one every time (checked against its API on
            // 2026-09-30). One checkout per order is kept by the order itself,
            // through its PaymentUrl.
            Preference preference = await new PreferenceClient().CreateAsync(
                MercadoPagoMapping.PreferenceFor(request, options.Value),
                RequestOptionsFor(),
                cancellationToken);

            return new PaymentCheckout(preference.Id, preference.InitPoint);
        }
        catch (MercadoPagoException exception)
        {
            LogCheckoutFailed(logger, exception, request.Order.Id);

            return PaymentErrors.GatewayUnavailable;
        }
    }

    public async Task<GatewayPayment?> FindPaymentAsync(string paymentId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(paymentId, out long id)) return null;

        try
        {
            Payment payment = await new PaymentClient().GetAsync(id, RequestOptionsFor(), cancellationToken);

            return MercadoPagoMapping.PaymentFrom(payment);
        }
        catch (MPNotFoundException)
        {
            return null;
        }
    }

    private RequestOptions RequestOptionsFor() => new() { AccessToken = options.Value.AccessToken };

    [LoggerMessage(Level = LogLevel.Error, Message = "Mercado Pago did not open a checkout for order {OrderId}.")]
    private static partial void LogCheckoutFailed(ILogger logger, Exception exception, Guid orderId);
}
