using System.Diagnostics;
using System.Text.Json;
using DrinkIt.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// Builds the real middleware chain in memory and runs requests through it, so
/// these cover what Program.cs wires rather than a handler called by hand.
/// Everything a framework-generated error response reaches the PWA with is
/// decided here: an unhandled exception, a route that matches nothing, a
/// request without a token.
/// </summary>
public class ProblemDetailsPipelineTests
{
    private const string SecretDetail = "Invalid column name 'PasswordHash' on table 'StaffUsers'";

    [Fact]
    public async Task Pipeline_WhenAnEndpointThrows_RespondsWithProblemJson()
    {
        HttpResponseSnapshot response = await Run(_ => throw new InvalidOperationException(SecretDetail));

        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            response.Body.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// The one that matters. A SqlException message names tables and columns; a
    /// DbUpdateException sometimes carries the connection string. CLAUDE.md and
    /// the endpoint skill both ask for a generic 500 that leaks nothing, with
    /// the real cause reaching the log instead.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenAnEndpointThrows_DoesNotLeakTheExceptionMessage()
    {
        HttpResponseSnapshot response = await Run(_ => throw new InvalidOperationException(SecretDetail));

        Assert.DoesNotContain(SecretDetail, response.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", response.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("at DrinkIt", response.Raw, StringComparison.Ordinal);
    }

    // Without this the response is untraceable: the user reports "me dio error"
    // and there is nothing to correlate against the logs.
    [Fact]
    public async Task Pipeline_WhenAnEndpointThrows_CarriesATraceId()
    {
        HttpResponseSnapshot response = await Run(_ => throw new InvalidOperationException(SecretDetail));

        Assert.True(response.Body.TryGetProperty("traceId", out JsonElement traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    /// <summary>
    /// RFC 9457 §3.1.2 types "instance" as a URI reference, exactly like "type".
    /// "POST /orders" is not one — the space is not even a legal character
    /// there — so the method stays out of it.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenAnEndpointThrows_PointsInstanceAtTheRequestPath()
    {
        HttpResponseSnapshot response = await Run(
            _ => throw new InvalidOperationException(SecretDetail),
            path: "/bar-alfa/orders/42");

        string instance = response.Body.GetProperty("instance").GetString()!;

        Assert.Equal("/bar-alfa/orders/42", instance);
        Assert.True(
            Uri.TryCreate(instance, UriKind.RelativeOrAbsolute, out _),
            $"'{instance}' is not a URI reference.");
    }

    /// <summary>
    /// Nothing venue-specific to say about a crash, so the framework default
    /// (a link to the HTTP spec) is left in place. ProblemTypes is for problems
    /// the domain can actually name.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenAnEndpointThrows_LeavesTheTypeToTheFrameworkDefault()
    {
        HttpResponseSnapshot response = await Run(_ => throw new InvalidOperationException(SecretDetail));

        Assert.Equal(
            "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            response.Body.GetProperty("type").GetString());
    }

    // Routing produces this one with an empty body unless status code pages is
    // in the pipeline. An empty body means the PWA's interceptor has to handle
    // two different contracts for the same failure.
    [Fact]
    public async Task Pipeline_WhenNothingMatchesTheRoute_RespondsWithProblemJson()
    {
        HttpResponseSnapshot response = await Run(StatusCode(StatusCodes.Status404NotFound));

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal(StatusCodes.Status404NotFound, response.Body.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// What a bartender hits when the token expires mid-shift. The JwtBearer
    /// middleware answers 401 with no body at all, so without status code pages
    /// this looks nothing like the 401 the login endpoint returns.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenTheTokenIsMissingOrExpired_RespondsWithProblemJson()
    {
        HttpResponseSnapshot response = await Run(StatusCode(StatusCodes.Status401Unauthorized));

        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal(StatusCodes.Status401Unauthorized, response.Body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Pipeline_WhenTheRequestSucceeds_LeavesTheResponseAlone()
    {
        HttpResponseSnapshot response = await Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return context.Response.WriteAsync("{\"ok\":true}");
        });

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("{\"ok\":true}", response.Raw);
    }

    /// <summary>
    /// The decision recorded in ADR-0008: one token, no refresh, so a shift that
    /// runs past the token's lifetime ends at the login screen. That makes this
    /// the only signal the PWA has to tell "your session ended, sign in again"
    /// apart from "those credentials are wrong", and both arrive as a 401.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenTheTokenExpired_SaysSoWithItsOwnType()
    {
        HttpResponseSnapshot response = await Run(
            StatusCode(StatusCodes.Status401Unauthorized),
            prepare: SessionExpiry.Mark);

        Assert.Equal(
            "urn:drinkit:problem:auth:session-expired",
            response.Body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Pipeline_WhenThereIsNoTokenAtAll_AsksForAuthentication()
    {
        HttpResponseSnapshot response = await Run(StatusCode(StatusCodes.Status401Unauthorized));

        Assert.Equal(
            "urn:drinkit:problem:auth:authentication-required",
            response.Body.GetProperty("type").GetString());
    }

    /// <summary>
    /// A KDS that types the administration address. The authorization policy
    /// answers 403 with no body, and unnamed it is indistinguishable to the PWA
    /// from a session that ran out. The first must not send anyone to the login
    /// screen: signing in again changes nothing about their role.
    /// </summary>
    [Fact]
    public async Task Pipeline_WhenTheRoleIsNotAllowedThere_SaysSoWithItsOwnType()
    {
        HttpResponseSnapshot response = await Run(StatusCode(StatusCodes.Status403Forbidden));

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:auth:forbidden", response.Body.GetProperty("type").GetString());
    }

    // The endpoint that already named its problem keeps it: a rejected login is
    // not an expired session, and overwriting it here would erase the one
    // distinction this whole change exists to make.
    [Fact]
    public async Task Pipeline_WhenTheEndpointAlreadyNamedTheProblem_LeavesItAlone()
    {
        HttpResponseSnapshot response = await Run(context =>
            TypedResults
                .Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: ProblemTypes.For("auth.invalid_credentials"))
                .ExecuteAsync(context));

        Assert.Equal(
            "urn:drinkit:problem:auth:invalid-credentials",
            response.Body.GetProperty("type").GetString());
    }

    private static RequestDelegate StatusCode(int statusCode) => context =>
    {
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    };

    private static async Task<HttpResponseSnapshot> Run(
        RequestDelegate endpoint,
        string path = "/bar-alfa/orders",
        Action<HttpContext>? prepare = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton(new DiagnosticListener("DrinkIt.Tests"));
        services.AddProblemDetailsForEveryError();

        await using ServiceProvider provider = services.BuildServiceProvider();

        ApplicationBuilder pipeline = new(provider);
        pipeline.UseProblemDetailsForEveryError();
        pipeline.Run(endpoint);

        using MemoryStream body = new();
        DefaultHttpContext context = new() { RequestServices = provider };
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        context.Response.Body = body;
        prepare?.Invoke(context);

        await pipeline.Build()(context);

        return HttpResponseSnapshot.Of(context, body);
    }
}
