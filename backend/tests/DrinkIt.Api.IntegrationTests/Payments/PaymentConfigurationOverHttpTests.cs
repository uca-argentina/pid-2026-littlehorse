using System.Net;
using System.Text.Json;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Payments;

/// <summary>
/// US-24: what the checkout needs to draw Mercado Pago's own button — the
/// public key, and nothing else. Read at run time, never baked into the build,
/// and never cached: a key rotated in Azure reaches the next phone that asks.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class PaymentConfigurationOverHttpTests(SqlServerFixture sql)
{
    private const string PublicKey = "APP_USR-0229e765-public-test-key";

    [Fact]
    public async Task PaymentConfiguration_WhenAPublicKeyIsConfigured_AnswersItUncached()
    {
        await using DrinkItApiFactory factory = new(sql.ConnectionString, services =>
            services.Configure<MercadoPagoOptions>(options => options.PublicKey = PublicKey));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/payments/configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(PublicKey, body.RootElement.GetProperty("publicKey").GetString());
    }

    // Development without Mercado Pago: the checkout falls back to its own button.
    [Fact]
    public async Task PaymentConfiguration_WhenNoPublicKeyIsConfigured_AnswersNone()
    {
        await using DrinkItApiFactory factory = new(sql.ConnectionString, services =>
            services.Configure<MercadoPagoOptions>(options => options.PublicKey = string.Empty));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/payments/configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("publicKey").ValueKind);
    }
}
