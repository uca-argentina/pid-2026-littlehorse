using System.Text;
using DrinkIt.Infrastructure.Authentication;
using DrinkIt.Infrastructure.Cashier;
using DrinkIt.Infrastructure.Kds;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

namespace DrinkIt.Api.Common;

/// <summary>
/// How a staff token is validated and what it is allowed to reach. Kept out of
/// Program.cs so it can be asserted: the settings below are the kind that fail
/// silently as a 403 nobody can explain.
/// </summary>
internal static class StaffAuthentication
{
    public static IServiceCollection AddStaffAuthentication(this IServiceCollection services, JwtOptions jwt)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),

                    // Match the short claim names the token is issued with.
                    RoleClaimType = JwtClaims.Role,
                    NameClaimType = JwtClaims.Name,
                };

                // Without this the handler rewrites "role" to the sixty-character
                // WS-Federation URI on its way in, while the two lines above still
                // point at "role". Nothing errors: the claim is simply not where
                // the policy looks, and every administrator gets a 403.
                options.MapInboundClaims = false;

                options.Events = new JwtBearerEvents
                {
                    // A browser's WebSocket API cannot set an Authorization
                    // header, so the KDS tablet's and the till's connections carry the token
                    // in the query string instead (US-15). Only for the hub's
                    // own path: every other request still authenticates the
                    // ordinary way, and a token sitting in a URL is exactly
                    // what query strings, proxies and access logs remember.
                    OnMessageReceived = context =>
                    {
                        StringValues accessToken = context.Request.Query["access_token"];

                        if (accessToken.Count > 0 && IsAHub(context.HttpContext.Request.Path))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            })
            .AddExpiredSessionDetection();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.Administrator, policy => policy.RequireRole(Policies.Administrator));
            options.AddPolicy(Policies.Kds, policy => policy.RequireRole(Policies.Kds));
            options.AddPolicy(Policies.Cashier, policy => policy.RequireRole(Policies.Cashier));
        });

        return services;
    }

    /// <summary>The two hubs a browser connects to: the bar's board and the till.</summary>
    private static bool IsAHub(PathString path) =>
        path.StartsWithSegments(KdsHubRoute.Path) || path.StartsWithSegments(TillHubRoute.Path);
}
