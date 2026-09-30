namespace DrinkIt.Infrastructure.Payments;

/// <summary>
/// Mercado Pago's Checkout Pro (US-24). The two secrets never go in a file that
/// is committed: in development they live in appsettings.Development.json,
/// which .gitignore keeps out; in Azure, in the Container App's secrets.
/// </summary>
public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    /// <summary>The application's access token, from the Prueba tab while testing. Secret.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The application's public key, from the same tab as the token. Not a
    /// secret: the checkout needs it to draw Mercado Pago's own button. Empty
    /// means the checkout falls back to its own button.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Webhooks → signature secret, to prove a notification came from Mercado Pago. Secret.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Where the PWA is served: the customer comes back to it from Mercado Pago's page.</summary>
    public string PublicAppUrl { get; set; } = string.Empty;

    /// <summary>
    /// Where the API is served, for Mercado Pago's notifications. Only a public
    /// HTTPS address is sent; a laptop cannot be reached, and a dead address is
    /// worse than none.
    /// </summary>
    public string PublicApiUrl { get; set; } = string.Empty;
}
