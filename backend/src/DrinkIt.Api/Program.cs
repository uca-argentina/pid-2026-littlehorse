using System.Text.Json.Serialization;
using DrinkIt.Api.Common;
using DrinkIt.Api.Extensions;
using DrinkIt.Api.Features.Authentication;
using DrinkIt.Api.Features.Menu;
using DrinkIt.Api.Features.Orders;
using DrinkIt.Api.Features.Staff;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Authentication;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Staff;
using DrinkIt.Infrastructure;
using DrinkIt.Infrastructure.Authentication;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<CreateStaffUserHandler>();
builder.Services.AddScoped<ChangeStaffUserRoleHandler>();
builder.Services.AddScoped<ResetStaffUserPasswordHandler>();
builder.Services.AddScoped<DeactivateStaffUserHandler>();
builder.Services.AddScoped<ReactivateStaffUserHandler>();
builder.Services.AddScoped<CreateProductHandler>();
builder.Services.AddScoped<UploadProductImageHandler>();
builder.Services.AddScoped<ConfirmOrderHandler>();

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

builder.Services.AddStaffAuthentication(jwt);

builder.Services.AddProblemDetailsForEveryError();
// Numbers are numbers on the wire. ASP.NET's default also reads them from
// strings, and the OpenAPI document says so — every price would reach the
// generated Angular client typed as "number | string".
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Every environment: a single venue, low traffic, and the Container App's managed
// identity already holds db_ddladmin (see infra/README.md), so there's no separate
// deploy step to run this from. See MigrationExtensions.ApplyMigrationsAsync for the
// accepted risk with more than one replica applying migrations at the same time.
await app.ApplyMigrationsAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await app.SeedDevelopmentDataAsync();
    app.MapScalarApiReference();
}

// First, so it wraps everything below: an exception thrown further down has to
// come back as problem+json, and so does a status the framework produces on its
// own (routing's 404, JwtBearer's empty 401). Otherwise the PWA has to cope with
// two different shapes for the same failure.
//
// In Development WebApplication puts the developer exception page ahead of this
// one, so a crash still shows its stack trace on a dev machine and only the
// deployed API answers with the generic body.
app.UseProblemDetailsForEveryError();

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
app.MapMenu();
app.MapOrders();
app.MapOrderTracking();
app.MapStaffUsers();
app.MapProducts();

await app.RunAsync();
