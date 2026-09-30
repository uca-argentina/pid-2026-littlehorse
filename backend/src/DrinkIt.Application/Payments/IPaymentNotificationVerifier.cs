namespace DrinkIt.Application.Payments;

/// <summary>
/// Whether a payment notification really came from the gateway (US-24).
/// Anybody can call the notification address; only the gateway can sign.
/// </summary>
public interface IPaymentNotificationVerifier
{
    /// <param name="signature">The <c>x-signature</c> header, as it arrived.</param>
    /// <param name="requestId">The <c>x-request-id</c> header, as it arrived.</param>
    /// <param name="dataId">The notified resource's id, from the <c>data.id</c> query parameter.</param>
    bool IsGenuine(string? signature, string? requestId, string? dataId);
}
