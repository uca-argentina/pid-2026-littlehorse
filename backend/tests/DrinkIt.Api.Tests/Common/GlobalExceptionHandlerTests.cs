using System.Text.Json;
using DrinkIt.Api.Common;
using DrinkIt.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.Tests.Common;

public class GlobalExceptionHandlerTests
{
    /// <summary>
    /// The domain guards what a username or a role may be, and a form can send
    /// something that breaks one of those rules. Letting that surface as a 500
    /// would report the administrator's typo as a server fault, and the screen
    /// would have nothing to show but "try again".
    /// </summary>
    [Fact]
    public async Task TryHandleAsync_WhenADomainInvariantIsBroken_RespondsWithBadRequestAndNamesTheRule()
    {
        HttpResponseSnapshot response = await Handle(
            new DomainException("staff.username_length", "Username must be 3-50 characters."));

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:staff:username-length", response.Text("type"));
        Assert.Equal("Username must be 3-50 characters.", response.Text("detail"));
    }

    // A domain message is written for us and names no table, column or secret.
    // Anything else is a bug, and its message is not ours to hand out.
    [Fact]
    public async Task TryHandleAsync_WhenAnythingElseFails_RespondsWithFiveHundredAndKeepsTheCauseOut()
    {
        HttpResponseSnapshot response = await Handle(
            new InvalidOperationException("Login failed for user 'sa' on server drinkit-sql."));

        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        Assert.DoesNotContain("drinkit-sql", response.Text("detail"), StringComparison.Ordinal);
        Assert.True(response.Body.TryGetProperty("traceId", out JsonElement traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    private static async Task<HttpResponseSnapshot> Handle(Exception exception)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddProblemDetailsForEveryError();

        await using ServiceProvider provider = services.BuildServiceProvider();
        using MemoryStream body = new();

        DefaultHttpContext context = new() { RequestServices = provider };
        context.Request.Path = "/staff/users";
        context.Request.Method = HttpMethods.Post;
        context.Response.Body = body;

        IExceptionHandler handler = provider.GetRequiredService<IExceptionHandler>();

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        return HttpResponseSnapshot.Of(context, body);
    }
}
