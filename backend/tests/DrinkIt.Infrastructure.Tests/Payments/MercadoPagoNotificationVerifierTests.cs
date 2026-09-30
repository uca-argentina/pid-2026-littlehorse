using System.Security.Cryptography;
using System.Text;
using DrinkIt.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Tests.Payments;

/// <summary>
/// US-24: a notification is only believed when Mercado Pago signed it. The
/// signature is built here the way Mercado Pago's documentation says it builds
/// it — "id:{data.id};request-id:{x-request-id};ts:{ts};", HMAC-SHA256 with the
/// webhook secret, in hex — and the SDK's own validator has to accept it.
/// </summary>
public class MercadoPagoNotificationVerifierTests
{
    private const string Secret = "a-webhook-secret";

    private const string RequestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e";

    private static readonly string Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string SignatureFor(string dataId, string secret = Secret)
    {
        string manifest = $"id:{dataId};request-id:{RequestId};ts:{Ts};";
        byte[] hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest));

        return $"ts={Ts},v1={Convert.ToHexStringLower(hash)}";
    }

    private static MercadoPagoNotificationVerifier AVerifier(string secret = Secret) =>
        new(Options.Create(new MercadoPagoOptions { WebhookSecret = secret }));

    [Fact]
    public void IsGenuine_WhenMercadoPagoSignedIt_SaysSo()
    {
        Assert.True(AVerifier().IsGenuine(SignatureFor("123456"), RequestId, "123456"));
    }

    // Signed for another payment: somebody reusing a real signature.
    [Fact]
    public void IsGenuine_WhenSignedForAnotherPayment_SaysNo()
    {
        Assert.False(AVerifier().IsGenuine(SignatureFor("123456"), RequestId, "999999"));
    }

    [Fact]
    public void IsGenuine_WhenSignedWithAnotherSecret_SaysNo()
    {
        Assert.False(AVerifier().IsGenuine(SignatureFor("123456", secret: "not-ours"), RequestId, "123456"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void IsGenuine_WhenTheSignatureIsMissingOrMalformed_SaysNo(string? signature)
    {
        Assert.False(AVerifier().IsGenuine(signature, RequestId, "123456"));
    }

    // Not configured is not "anything goes": nothing is believed.
    [Fact]
    public void IsGenuine_WhenNoSecretIsConfigured_SaysNo()
    {
        Assert.False(AVerifier(secret: "").IsGenuine(SignatureFor("123456", secret: ""), RequestId, "123456"));
    }
}
