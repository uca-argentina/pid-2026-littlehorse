using DrinkIt.Infrastructure.Payments;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Tests.Payments;

/// <summary>
/// /mp-review, 2026-09-30: outside a developer's laptop, a Mercado Pago
/// configuration that cannot work refuses to start, instead of failing at the
/// first payment — Mercado Pago drops an http return address without a word.
/// </summary>
public class MercadoPagoOptionsValidationTests
{
    private static MercadoPagoOptions Configured(Action<MercadoPagoOptions>? change = null)
    {
        MercadoPagoOptions options = new()
        {
            AccessToken = "APP_USR-test",
            WebhookSecret = "a-secret",
            PublicAppUrl = "https://drinkit.example.com",
            PublicApiUrl = "https://api.drinkit.example.com",
        };
        change?.Invoke(options);

        return options;
    }

    private static ValidateOptionsResult Validate(MercadoPagoOptions options, string environment) =>
        new MercadoPagoOptionsValidation(new FakeEnvironment(environment)).Validate(null, options);

    // The rest of the team works without Mercado Pago: nothing is required here.
    [Fact]
    public void Validate_InDevelopment_AcceptsAnything()
    {
        Assert.True(Validate(new MercadoPagoOptions(), Environments.Development).Succeeded);
    }

    // No token: digital payments are off (503), and the rest of the app still runs.
    [Fact]
    public void Validate_OutsideDevelopmentWithoutAToken_Accepts()
    {
        Assert.True(Validate(new MercadoPagoOptions(), Environments.Production).Succeeded);
    }

    [Fact]
    public void Validate_OutsideDevelopmentWithEverythingInPlace_Accepts()
    {
        Assert.True(Validate(Configured(), Environments.Production).Succeeded);
    }

    [Theory]
    [InlineData("http://drinkit.example.com")]
    [InlineData("https://localhost:4200")]
    [InlineData("")]
    public void Validate_OutsideDevelopmentWithAReturnAddressMercadoPagoWouldDrop_Refuses(string appUrl)
    {
        ValidateOptionsResult result = Validate(Configured(options => options.PublicAppUrl = appUrl), Environments.Production);

        Assert.True(result.Failed);
        Assert.Contains("PublicAppUrl", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_OutsideDevelopmentWithAnUnreachableApi_Refuses()
    {
        ValidateOptionsResult result = Validate(Configured(options => options.PublicApiUrl = "http://localhost:5127"), Environments.Production);

        Assert.Contains("PublicApiUrl", result.FailureMessage, StringComparison.Ordinal);
    }

    // Without it every notification is refused, and paid orders expire.
    [Fact]
    public void Validate_OutsideDevelopmentWithoutTheWebhookSecret_Refuses()
    {
        ValidateOptionsResult result = Validate(Configured(options => options.WebhookSecret = ""), Environments.Production);

        Assert.Contains("WebhookSecret", result.FailureMessage, StringComparison.Ordinal);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "DrinkIt.Api";

        public string ContentRootPath { get; set; } = string.Empty;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
