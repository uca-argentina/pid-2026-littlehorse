using System.Text;
using DrinkIt.Api.Extensions;
using DrinkIt.Api.Features.Authentication;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Authentication;
using DrinkIt.Application.Common;
using DrinkIt.Infrastructure;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<LoginHandler>();

// Both names resolve to the same per-request instance: the middleware writes to
// it and the DbContext reads from it while handling the same request.
builder.Services.AddScoped<CurrentVenue>();
builder.Services.AddScoped<ICurrentVenue>(services => services.GetRequiredService<CurrentVenue>());

JwtOptions jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (!builder.Environment.IsDevelopment() && jwt.SigningKey.StartsWith("dev-", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is still the development placeholder. Set a real key outside Development.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
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
    });

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await app.ApplyMigrationsAsync();
    await app.SeedDevelopmentDataAsync();
    app.MapScalarApiReference();
}

// HSTS tells returning browsers to skip http entirely next time, instead of
// depending on the redirect below every single request. Off in Development:
// its default max-age is long enough to be annoying against a dev cert that
// gets regenerated.
if (!app.Environment.IsDevelopment()) app.UseHsts();

app.UseHttpsRedirection();
// Order matters. Routing first so route values exist, then authentication so
// the claims exist, and only then the venue resolution that reads both. Nothing
// touching the database may run before it.
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<VenueResolutionMiddleware>();
app.UseAuthorization();

app.MapLogin();

await app.RunAsync();
