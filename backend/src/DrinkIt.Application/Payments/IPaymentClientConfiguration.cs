namespace DrinkIt.Application.Payments;

/// <summary>
/// What the customer's phone needs to draw the gateway's own payment button
/// (US-24, Mercado Pago's Wallet Brick). Public by nature: nothing here may
/// ever be a secret.
/// </summary>
public interface IPaymentClientConfiguration
{
    /// <summary>The gateway's public key, or null when none is configured.</summary>
    string? PublicKey { get; }
}
