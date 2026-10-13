using DrinkIt.Application.Common;
using DrinkIt.Infrastructure.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DrinkIt.Api.Tenancy;

/// <summary>
/// Who is signed in on this request, read from the token's name claim (US-30)
/// and its subject (US-36).
/// Registered scoped like <see cref="CurrentVenue"/>, and null outside a
/// request: the seeder at startup has no user, and says so.
/// </summary>
internal sealed class CurrentStaffUser(IHttpContextAccessor accessor) : ICurrentStaffUser
{
    public string? Username =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } principal
            ? principal.FindFirst(JwtClaims.Name)?.Value
            : null;

    public Guid? Id =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } principal
            && Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out Guid id)
            ? id
            : null;
}
