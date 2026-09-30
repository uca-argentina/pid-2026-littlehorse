using DrinkIt.Application.Payments;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Payments;

/// <summary>The public half of the Mercado Pago configuration, and only that half.</summary>
internal sealed class MercadoPagoClientConfiguration(IOptions<MercadoPagoOptions> options) : IPaymentClientConfiguration
{
    public string? PublicKey => string.IsNullOrWhiteSpace(options.Value.PublicKey) ? null : options.Value.PublicKey;
}
