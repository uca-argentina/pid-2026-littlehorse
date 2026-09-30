using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Payments;

/// <summary>
/// Checked when the app starts (/mp-review, 2026-09-30): outside a developer's
/// laptop, a Mercado Pago configuration that cannot work refuses to start
/// rather than failing at the first payment.
/// </summary>
/// <remarks>
/// Without a token digital payments are simply off — they answer 503 — so a
/// deploy that has not configured Mercado Pago yet still serves everything
/// else. With one, everything it needs has to be there: Mercado Pago drops an
/// http return address without a word, and without the webhook secret every
/// notification is refused.
/// </remarks>
internal sealed class MercadoPagoOptionsValidation(IHostEnvironment environment) : IValidateOptions<MercadoPagoOptions>
{
    public ValidateOptionsResult Validate(string? name, MercadoPagoOptions options)
    {
        if (environment.IsDevelopment() || string.IsNullOrWhiteSpace(options.AccessToken)) return ValidateOptionsResult.Success;

        List<string> problems = [];

        if (!MercadoPagoMapping.IsPublic(options.PublicAppUrl))
            problems.Add("MercadoPago:PublicAppUrl must be a public https address: Mercado Pago drops any other return address.");

        if (!MercadoPagoMapping.IsPublic(options.PublicApiUrl))
            problems.Add("MercadoPago:PublicApiUrl must be a public https address: Mercado Pago cannot notify any other.");

        if (string.IsNullOrWhiteSpace(options.WebhookSecret))
            problems.Add("MercadoPago:WebhookSecret is required: without it every notification is refused.");

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
