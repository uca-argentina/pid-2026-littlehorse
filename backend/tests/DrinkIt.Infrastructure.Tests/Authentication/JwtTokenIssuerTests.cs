using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DrinkIt.Infrastructure.Tests.Authentication;

public class JwtTokenIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 23, 0, 0, TimeSpan.Zero);
    private static readonly Guid StaffUserId = Guid.CreateVersion7();
    private static readonly Guid VenueId = Guid.CreateVersion7();

    private readonly JwtTokenIssuer _issuer = new(
        Options.Create(new JwtOptions
        {
            SigningKey = "a signing key that is comfortably longer than 32 bytes",
            Issuer = "drinkit-test",
            Audience = "drinkit-test",
            LifetimeMinutes = 60,
        }),
        new FrozenClock(Now));

    [Fact]
    public void Issue_WhenCalled_ProducesAReadableJwt()
    {
        AccessToken token = Issue();

        JsonWebToken parsed = new(token.Value);

        Assert.Equal("drinkit-test", parsed.Issuer);
        Assert.Equal(StaffUserId.ToString(), parsed.Subject);
    }

    // The whole point of the token: from here on the tenant comes from this
    // claim and never from the URL.
    [Fact]
    public void Issue_WhenCalled_CarriesTheVenue()
    {
        JsonWebToken parsed = new(Issue().Value);

        Assert.Equal(VenueId.ToString(), parsed.GetClaim(JwtClaims.Venue).Value);
    }

    [Fact]
    public void Issue_WhenCalled_CarriesTheRole()
    {
        JsonWebToken parsed = new(Issue().Value);

        Assert.Equal(nameof(StaffRole.Administrator), parsed.GetClaim(JwtClaims.Role).Value);
    }

    [Fact]
    public void Issue_WhenCalled_ExpiresAfterTheConfiguredLifetime()
    {
        AccessToken token = Issue();

        Assert.Equal(Now.AddMinutes(60), token.ExpiresAt);
    }

    private AccessToken Issue() =>
        _issuer.Issue(StaffUserId, VenueId, "euge", StaffRole.Administrator);

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
