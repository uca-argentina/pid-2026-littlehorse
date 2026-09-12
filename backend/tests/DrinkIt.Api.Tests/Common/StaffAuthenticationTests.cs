using System.Security.Claims;
using DrinkIt.Api.Common;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
        AuthorizationResult result = await Authorize(StaffRole.Administrator);

        Assert.True(result.Succeeded);
    }

    // US-03, criterion 6, written by exclusion: a role added later is refused
    // until somebody decides otherwise, instead of being let in by omission.
    [Theory]
    [InlineData(StaffRole.Kds)]
    [InlineData(StaffRole.Waiter)]
    public async Task AdministratorPolicy_WhenTheTokenSaysAnythingElse_Refuses(StaffRole role)
    {
        AuthorizationResult result = await Authorize(role);

        Assert.False(result.Succeeded);
    }

    private static async Task<AuthorizationResult> Authorize(StaffRole role)
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
            .AuthorizeAsync(user, resource: null, Policies.Administrator);
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
