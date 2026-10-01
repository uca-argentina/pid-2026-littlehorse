using System.Security.Claims;
using DrinkIt.Api.Common;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using DrinkIt.Infrastructure.Cashier;
using DrinkIt.Infrastructure.Kds;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// The wiring that decides who reaches the administration endpoints. Asserted
/// rather than trusted: every way of getting this wrong ends in the same 403,
/// with nothing in the response or the log that says which setting did it.
/// </summary>
public class StaffAuthenticationTests
{
    /// <summary>
    /// The handler rewrites well-known short claim names to their
    /// WS-Federation URIs unless told otherwise. With the mapping on, "role"
    /// arrives as a sixty-character URI while the validation parameters still
    /// look for "role", and every authenticated administrator is refused.
    /// </summary>
    [Fact]
    public void AddStaffAuthentication_WhenConfigured_KeepsTheShortClaimNamesTheTokenCarries()
    {
        JwtBearerOptions options = ConfiguredBearerOptions();

        Assert.False(options.MapInboundClaims);
        Assert.Equal(JwtClaims.Role, options.TokenValidationParameters.RoleClaimType);
        Assert.Equal(JwtClaims.Name, options.TokenValidationParameters.NameClaimType);
    }

    [Fact]
    public async Task AdministratorPolicy_WhenTheTokenSaysAdministrator_Allows()
    {
        AuthorizationResult result = await Authorize(StaffRole.Administrator, Policies.Administrator);

        Assert.True(result.Succeeded);
    }

    // US-03, criterion 6, written by exclusion: a role added later is refused
    // until somebody decides otherwise, instead of being let in by omission.
    [Theory]
    [InlineData(StaffRole.Kds)]
    [InlineData(StaffRole.Waiter)]
    [InlineData(StaffRole.Cashier)]
    public async Task AdministratorPolicy_WhenTheTokenSaysAnythingElse_Refuses(StaffRole role)
    {
        AuthorizationResult result = await Authorize(role, Policies.Administrator);

        Assert.False(result.Succeeded);
    }

    // US-15, criterion 3: the bar's own tablet, and only the bar's own tablet.
    [Fact]
    public async Task KdsPolicy_WhenTheTokenSaysKds_Allows()
    {
        AuthorizationResult result = await Authorize(StaffRole.Kds, Policies.Kds);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(StaffRole.Administrator)]
    [InlineData(StaffRole.Waiter)]
    [InlineData(StaffRole.Cashier)]
    public async Task KdsPolicy_WhenTheTokenSaysAnythingElse_Refuses(StaffRole role)
    {
        AuthorizationResult result = await Authorize(role, Policies.Kds);

        Assert.False(result.Succeeded);
    }

    // US-26: the till, and only the till.
    [Fact]
    public async Task CashierPolicy_WhenTheTokenSaysCashier_Allows()
    {
        AuthorizationResult result = await Authorize(StaffRole.Cashier, Policies.Cashier);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(StaffRole.Administrator)]
    [InlineData(StaffRole.Kds)]
    [InlineData(StaffRole.Waiter)]
    public async Task CashierPolicy_WhenTheTokenSaysAnythingElse_Refuses(StaffRole role)
    {
        AuthorizationResult result = await Authorize(role, Policies.Cashier);

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// US-15: a browser cannot set a header on a WebSocket or EventSource
    /// connection, so the KDS hub's own OnMessageReceived reads the token from
    /// the query string instead. AddExpiredSessionDetection used to assign a
    /// whole new JwtBearerEvents on top of it — same options instance, later
    /// in the chain — which silently threw that handler away: every browser
    /// transport but long polling got a 401, and the only sign of it was a
    /// tablet whose board never updated by itself.
    /// </summary>
    [Fact]
    public async Task AddStaffAuthentication_WhenTheHubConnectionCarriesTheTokenInTheQueryString_StillReadsIt()
    {
        JwtBearerOptions options = ConfiguredBearerOptions();

        DefaultHttpContext context = new();
        context.Request.Path = KdsHubRoute.Path;
        context.Request.QueryString = new QueryString("?access_token=a-hub-connection-token");

        AuthenticationScheme scheme = new(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        MessageReceivedContext messageReceived = new(context, scheme, options);

        await options.Events!.MessageReceived(messageReceived);

        Assert.Equal("a-hub-connection-token", messageReceived.Token);
    }

    // US-26: the till's own hub, reached by a browser the same way.
    [Fact]
    public async Task AddStaffAuthentication_WhenTheTillsHubConnectionCarriesTheTokenInTheQueryString_ReadsIt()
    {
        JwtBearerOptions options = ConfiguredBearerOptions();

        DefaultHttpContext context = new();
        context.Request.Path = TillHubRoute.Path;
        context.Request.QueryString = new QueryString("?access_token=a-till-connection-token");

        AuthenticationScheme scheme = new(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        MessageReceivedContext messageReceived = new(context, scheme, options);

        await options.Events!.MessageReceived(messageReceived);

        Assert.Equal("a-till-connection-token", messageReceived.Token);
    }

    private static async Task<AuthorizationResult> Authorize(StaffRole role, string policy)
    {
        await using ServiceProvider provider = Configured();

        // Built the way the bearer handler builds it once the mapping is off:
        // a claim named "role", on an identity that knows to read roles there.
        ClaimsPrincipal user = new(
            new ClaimsIdentity(
                [new Claim(JwtClaims.Role, role.ToString())],
                authenticationType: JwtBearerDefaults.AuthenticationScheme,
                nameType: JwtClaims.Name,
                roleType: JwtClaims.Role));

        return await provider
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, resource: null, policy);
    }

    private static JwtBearerOptions ConfiguredBearerOptions()
    {
        using ServiceProvider provider = Configured();

        return provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static ServiceProvider Configured()
    {
        ServiceCollection services = new();
        services.AddLogging();

        // Any key of at least 32 bytes: HMAC-SHA256 refuses shorter ones, and
        // nothing here signs or verifies a real token.
        services.AddStaffAuthentication(new JwtOptions
        {
            SigningKey = "a-key-long-enough-for-hmac-sha256-in-a-test",
        });

        return services.BuildServiceProvider();
    }
}
