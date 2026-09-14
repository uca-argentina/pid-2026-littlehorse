using System.Text;
using System.Text.Json;
using DrinkIt.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// What the PWA actually receives: status, content type, the bytes as they went
/// out, and those bytes parsed when they were JSON.
/// </summary>
public sealed record HttpResponseSnapshot(int StatusCode, string ContentType, string Raw, JsonElement Body)
{
    public static HttpResponseSnapshot Of(HttpContext context, Stream body)
    {
        string raw = Encoding.UTF8.GetString(((MemoryStream)body).ToArray());

        return new HttpResponseSnapshot(
            context.Response.StatusCode,
            context.Response.ContentType ?? string.Empty,
            raw,
            raw.StartsWith('{') || raw.StartsWith('[') ? JsonDocument.Parse(raw).RootElement.Clone() : default);
    }

    public string Text(string property) => Body.GetProperty(property).GetString()!;
}

/// <summary>
/// Runs an endpoint's IResult against a real HttpContext, so the assertions are
/// about bytes and headers rather than an in-memory object. That is the point
/// when the contract is RFC 9457 and the Angular client is generated from it.
/// </summary>
public static class EndpointResponse
{
    public static async Task<HttpResponseSnapshot> Execute(IResult result, string path, string method)
    {
        ServiceCollection services = new();
        services.AddLogging();

        // The real registration, not a bare AddProblemDetails: it installs the
        // customization that names an unnamed 401 and stamps the traceId,
        // exactly as Program.cs does.
        services.AddProblemDetailsForEveryError();

        await using ServiceProvider provider = services.BuildServiceProvider();
        using MemoryStream body = new();

        DefaultHttpContext context = new() { RequestServices = provider };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        return HttpResponseSnapshot.Of(context, body);
    }
}
