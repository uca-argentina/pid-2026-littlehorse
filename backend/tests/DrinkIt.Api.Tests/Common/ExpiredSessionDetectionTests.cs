using DrinkIt.Api.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// The seam between JwtBearer and the 401 body. Without it every test about an
/// expired session still passes while the detection is dead in the deployed API,
/// because nothing else ever marks the request.
/// </summary>
public class ExpiredSessionDetectionTests
{
    [Fact]
    public async Task OnAuthenticationFailed_WhenTheTokenExpired_MarksTheRequest()
    {
        DefaultHttpContext context = new();

        await FailAuthenticationWith(new SecurityTokenExpiredException(), context);

        Assert.True(SessionExpiry.HasExpired(context));
    }

    // A tampered signature is not an expired session: that request never had a
    // valid token, and telling the bartender "your session ended" would be a lie.
    [Fact]
    public async Task OnAuthenticationFailed_WhenTheTokenIsInvalid_LeavesTheRequestUnmarked()
    {
        DefaultHttpContext context = new();

        await FailAuthenticationWith(new SecurityTokenInvalidSignatureException(), context);

        Assert.False(SessionExpiry.HasExpired(context));
    }

    private static async Task FailAuthenticationWith(Exception exception, HttpContext context)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddExpiredSessionDetection();

        await using ServiceProvider provider = services.BuildServiceProvider();

        JwtBearerOptions options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        AuthenticationScheme scheme = new(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

        await options.Events!.AuthenticationFailed(
            new AuthenticationFailedContext(context, scheme, options) { Exception = exception });
    }
}
