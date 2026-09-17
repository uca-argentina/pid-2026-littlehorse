using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace DrinkIt.Api.Common;

internal static class JwtBearerEventsExtensions
{
    /// <summary>
    /// Records that an otherwise valid token simply ran out. This is the only
    /// point that still knows it: the middleware turns every failure into the
    /// same empty 401, and the body is written much later, by then with nothing
    /// left to tell an expired session from a request that never had a token.
    /// </summary>
    public static AuthenticationBuilder AddExpiredSessionDetection(this AuthenticationBuilder builder)
    {
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    if (context.Exception is SecurityTokenExpiredException) SessionExpiry.Mark(context.HttpContext);

                    return Task.CompletedTask;
                },
            });

        return builder;
    }
}
