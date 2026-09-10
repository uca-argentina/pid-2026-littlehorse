using System.Text.Json;
using DrinkIt.Api.Common;
using DrinkIt.Api.Features.Authentication;
using DrinkIt.Application.Authentication;
using DrinkIt.Application.Security;
using DrinkIt.Domain.Staff;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.Tests.Features.Authentication;

/// <summary>
/// Executes the endpoint's IResult against a real HttpContext, so these assert
/// the bytes and headers the PWA receives rather than an in-memory object. That
/// matters here: the wire contract is the whole point of RFC 9457.
/// </summary>
public class LoginEndpointTests
{
    private const string RightPassword = "the right password";

    private static readonly StaffCredentials Administrator = new(
        StaffUserId: Guid.CreateVersion7(),
        VenueId: Guid.CreateVersion7(),
        Username: "euge",
        PasswordHash: $"hashed:{RightPassword}",
        Role: StaffRole.Administrator,
        IsActive: true);

    [Fact]
    public async Task HandleAsync_WhenCredentialsAreWrong_RespondsWithProblemJson()
    {
        HttpResponseSnapshot response = await Login("euge", "wrong");

        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
    }

    /// <summary>
    /// RFC 9457 §3.1.1 defines "type" as a URI reference, and says a relative
    /// one is resolved against the document's base URI. A bare code like
    /// "auth.invalid_credentials" parses as a relative reference, so nothing
    /// rejects it — it just resolves to a different absolute URI per host,
    /// which is the opposite of the stable identifier the PWA branches on.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenCredentialsAreWrong_IdentifiesTheProblemWithAnAbsoluteUri()
    {
        HttpResponseSnapshot response = await Login("euge", "wrong");

        string type = response.Body.GetProperty("type").GetString()!;

        Assert.True(
            Uri.TryCreate(type, UriKind.Absolute, out _),
            $"'{type}' is not an absolute URI, so RFC 9457 consumers resolve it against the host.");
        Assert.Equal("urn:drinkit:problem:auth:invalid-credentials", type);
    }

    [Fact]
    public async Task HandleAsync_WhenCredentialsAreWrong_KeepsTheMessageOutOfTheIdentifier()
    {
        HttpResponseSnapshot response = await Login("euge", "wrong");

        Assert.Equal("Authentication failed", response.Body.GetProperty("title").GetString());
        Assert.Equal(
            LoginHandler.InvalidCredentials.Message,
            response.Body.GetProperty("detail").GetString());
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            response.Body.GetProperty("status").GetInt32());
    }

    // Correlates the response the user saw with the server logs. Only its
    // presence is asserted: the value comes from the ambient Activity when
    // there is one and from the request id when there is not.
    [Fact]
    public async Task HandleAsync_WhenCredentialsAreWrong_CarriesATraceId()
    {
        HttpResponseSnapshot response = await Login("euge", "wrong");

        Assert.True(response.Body.TryGetProperty("traceId", out JsonElement traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    // Pins the success shape too, because the generated Angular client is built
    // from it: renaming a property here silently breaks the front.
    [Fact]
    public async Task HandleAsync_WhenCredentialsAreValid_RespondsWithTheSession()
    {
        HttpResponseSnapshot response = await Login("euge", RightPassword);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("euge", response.Body.GetProperty("username").GetString());
        Assert.Equal("Administrator", response.Body.GetProperty("role").GetString());
        Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("token").GetString()));
        Assert.True(response.Body.GetProperty("expiresAt").TryGetDateTimeOffset(out _));
    }

    private static async Task<HttpResponseSnapshot> Login(string username, string password)
    {
        LoginHandler handler = new(
            new Fake.CredentialsQuery(Administrator),
            new Fake.PasswordHasher(),
            new Fake.TokenIssuer());

        IResult result = await LoginEndpoint.HandleAsync(
            new LoginRequest(username, password), handler, CancellationToken.None);

        return await Execute(result);
    }

    /// <summary>
    /// Writes the result to a real response. ProblemDetails is registered
    /// because that is what stamps the traceId, exactly as Program.cs does.
    /// </summary>
    private static async Task<HttpResponseSnapshot> Execute(IResult result)
    {
        ServiceCollection services = new();
        services.AddLogging();

        // The real registration, not a bare AddProblemDetails: it installs the
        // customization that names an unnamed 401, and these assert that a
        // rejected login keeps the type the endpoint gave it.
        services.AddProblemDetailsForEveryError();

        await using ServiceProvider provider = services.BuildServiceProvider();
        using MemoryStream body = new();

        DefaultHttpContext context = new() { RequestServices = provider };
        context.Request.Path = "/bar-alfa/auth/login";
        context.Request.Method = HttpMethods.Post;
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        return new HttpResponseSnapshot(
            context.Response.StatusCode,
            context.Response.ContentType ?? string.Empty,
            JsonDocument.Parse(body.ToArray()).RootElement.Clone());
    }

    private sealed record HttpResponseSnapshot(int StatusCode, string ContentType, JsonElement Body);

    private static class Fake
    {
        public sealed class CredentialsQuery(StaffCredentials found) : IStaffCredentialsQuery
        {
            public Task<StaffCredentials?> FindAsync(string username, CancellationToken cancellationToken) =>
                Task.FromResult(username == found.Username ? found : null);
        }

        public sealed class PasswordHasher : IPasswordHasher
        {
            public string Hash(string password) => $"hashed:{password}";

            public bool Verify(string password, string hash) => hash == Hash(password);
        }

        public sealed class TokenIssuer : ITokenIssuer
        {
            public AccessToken Issue(Guid staffUserId, Guid venueId, string username, StaffRole role) =>
                new($"token:{staffUserId}", DateTimeOffset.UtcNow.AddHours(8));
        }
    }
}
