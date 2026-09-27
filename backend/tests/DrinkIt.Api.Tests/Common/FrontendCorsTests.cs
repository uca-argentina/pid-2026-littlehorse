using DrinkIt.Api.Common;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// The PWA and the API live on different hosts once deployed, so the browser
/// asks the API before every call. Asserted rather than trusted: a missing
/// origin shows up as a CORS error in the browser console and nowhere else.
/// </summary>
public class FrontendCorsTests
{
    private const string Pwa = "https://pwa.example.net";

    [Fact]
    public async Task AddFrontendCors_WhenTheOriginIsConfigured_AllowsTheLoginPreflight()
    {
        CorsResult result = await Preflight(Pwa, allowedOrigins: [Pwa]);

        Assert.Equal(Pwa, result.AllowedOrigin);
        Assert.Contains("POST", result.AllowedMethods);
        Assert.Contains("authorization", result.AllowedHeaders);
        Assert.Contains("content-type", result.AllowedHeaders);
    }

    // The custom domain arrives as a second origin: both keep working while
    // the old address is still around.
    [Fact]
    public async Task AddFrontendCors_WhenSeveralOriginsAreConfigured_AllowsEachOfThem()
    {
        const string customDomain = "https://drinkit.example.com";

        CorsResult result = await Preflight(customDomain, allowedOrigins: [Pwa, customDomain]);

        Assert.Equal(customDomain, result.AllowedOrigin);
    }

    [Fact]
    public async Task AddFrontendCors_WhenTheOriginIsNotConfigured_RefusesIt()
    {
        CorsResult result = await Preflight("https://evil.example.org", allowedOrigins: [Pwa]);

        Assert.False(result.IsOriginAllowed);
    }

    // Development goes through the Angular proxy, same origin, so it
    // configures none and the API answers no cross-origin request at all.
    [Fact]
    public async Task AddFrontendCors_WhenNoOriginIsConfigured_RefusesEveryOrigin()
    {
        CorsResult result = await Preflight(Pwa, allowedOrigins: []);

        Assert.False(result.IsOriginAllowed);
    }

    private static async Task<CorsResult> Preflight(string origin, string[] allowedOrigins)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(allowedOrigins.Select((allowed, index) =>
                new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{index}", allowed)))
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddFrontendCors(configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();

        DefaultHttpContext context = new() { RequestServices = provider };
        context.Request.Method = HttpMethods.Options;
        context.Request.Headers.Origin = origin;
        context.Request.Headers.AccessControlRequestMethod = "POST";
        context.Request.Headers.AccessControlRequestHeaders = "authorization,content-type";

        CorsPolicy? policy = await provider.GetRequiredService<ICorsPolicyProvider>().GetPolicyAsync(context, policyName: null);

        return provider.GetRequiredService<ICorsService>().EvaluatePolicy(context, policy!);
    }
}
