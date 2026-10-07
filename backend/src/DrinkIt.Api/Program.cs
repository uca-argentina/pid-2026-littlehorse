using System.Text.Json.Serialization;
using DrinkIt.Api.Common;
using DrinkIt.Api.Extensions;
using DrinkIt.Api.Features.Authentication;
using DrinkIt.Api.Features.Cashier;
using DrinkIt.Api.Features.Kds;
using DrinkIt.Api.Features.Menu;
using DrinkIt.Api.Features.Nights;
using DrinkIt.Api.Features.Orders;
using DrinkIt.Api.Features.Staff;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Common;
using DrinkIt.Infrastructure;
using DrinkIt.Infrastructure.Authentication;
using DrinkIt.Infrastructure.Cashier;
using DrinkIt.Infrastructure.Kds;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
// One line per feature: each one lists its own handlers, next to its endpoints.
builder.Services.AddLoginHandlers();
builder.Services.AddStaffHandlers();
builder.Services.AddNightHandlers();
builder.Services.AddMenuHandlers();
builder.Services.AddOrderHandlers();
builder.Services.AddKdsHandlers();
builder.Services.AddCashierHandlers();

// Both names resolve to the same per-request instance: the middleware writes to
// it and the DbContext reads from it while handling the same request.
builder.Services.AddScoped<CurrentVenue>();
builder.Services.AddScoped<ICurrentVenue>(services => services.GetRequiredService<CurrentVenue>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentStaffUser, CurrentStaffUser>();

JwtOptions jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (!builder.Environment.IsDevelopment() && jwt.SigningKey.StartsWith("dev-", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is still the development placeholder. Set a real key outside Development.");
}

builder.Services.AddStaffAuthentication(jwt);
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddTelemetry(builder.Configuration);

builder.Services.AddProblemDetailsForEveryError();
// Numbers are numbers on the wire. ASP.NET's default also reads them from
// strings, and the OpenAPI document says so — every price would reach the
// generated Angular client typed as "number | string".
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<ContractEnumSchemaTransformer>());

WebApplication app = builder.Build();

// Every environment: a single venue, low traffic, and the Container App's managed
// identity already holds db_ddladmin (see infra/README.md), so there's no separate
// deploy step to run this from. See MigrationExtensions.ApplyMigrationsAsync for the
// accepted risk with more than one replica applying migrations at the same time.
await app.ApplyMigrationsAsync();

// Every environment too: it is what creates the first administrator in
// production, where there is no sign-up. It does nothing unless
// Bootstrap:AdminPassword is set, see BootstrapSeeder.
await app.SeedBootstrapDataAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
// Before authentication: the browser's preflight carries no token, and refusing
// it with a 401 would block the real request behind it.
app.UseCors();
// Without this the WebSockets transport fails outright — "the connection ID
// is not present on the server" — before KdsHub sees a single request: the
// upgrade itself is refused below this middleware. SignalR falls back to
// long polling, but the bar's tablet has no business paying for that latency
// every night when the one line above fixes it (US-15).
app.UseWebSockets();
app.UseAuthentication();
app.UseMiddleware<VenueResolutionMiddleware>();
app.UseAuthorization();

app.MapLogin();
app.MapMenu();
app.MapOrders();
app.MapOrderTracking();
app.MapStaffUsers();
app.MapNights();
app.MapCategories();
app.MapProducts();
app.MapKds();
app.MapHub<KdsHub>(KdsHubRoute.Path);
app.MapHub<TillHub>(TillHubRoute.Path);
app.MapCashier();

await app.RunAsync();

// Top-level statements generate an internal Program by default. Public and
// partial so WebApplicationFactory<Program> can find it from the test project
// — needed once, for the KDS hub's own isolation test (US-15): nothing before
// it in this codebase has spun up the whole app.
public partial class Program;
