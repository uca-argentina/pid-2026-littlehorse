using System.Security.Claims;
using System.Text;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DrinkIt.Infrastructure.Authentication;

internal sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : ITokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Issue(Guid staffUserId, Guid venueId, string username, StaffRole role)
    {
        JwtOptions settings = options.Value;
        DateTimeOffset issuedAt = clock.GetUtcNow();
        DateTimeOffset expiresAt = issuedAt.AddMinutes(settings.LifetimeMinutes);

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, staffUserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
                new Claim(JwtClaims.Name, username),
                new Claim(JwtClaims.Venue, venueId.ToString()),
                new Claim(JwtClaims.Role, role.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(Handler.CreateToken(descriptor), expiresAt);
    }
}
