using DrinkIt.Application.Nights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DrinkIt.Api.Common;

/// <summary>
/// US-35, criterion 4: the account works the venue's night. Part of the KDS's
/// and the till's own policies, so every endpoint and hub that names one of
/// them checks it, and none can forget to.
/// </summary>
internal sealed class TonightsCrewRequirement : IAuthorizationRequirement;

/// <summary>
/// Asks the night, through <see cref="ITonightsCrew"/>, whether the account in
/// the token is in its crew. The rule itself — which night counts — lives
/// behind that port; this only adapts it to authorization.
/// </summary>
internal sealed class TonightsCrewHandler(ITonightsCrew crew, TimeProvider clock, IHttpContextAccessor accessor)
    : AuthorizationHandler<TonightsCrewRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TonightsCrewRequirement requirement)
    {
        // The wrong role is answered as the wrong role, and costs no query.
        if (context.PendingRequirements.OfType<RolesAuthorizationRequirement>().Any()) return;

        string? subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (Guid.TryParse(subject, out Guid staffUserId) && await crew.IncludesAsync(staffUserId, clock.GetUtcNow(), CancellationToken.None))
        {
            context.Succeed(requirement);

            return;
        }

        if (accessor.HttpContext is HttpContext http) NotInTonightsCrew.Mark(http);
    }
}

/// <summary>
/// Remembers, for the problem written afterwards, that a 403 was about the
/// night and not about the role: the screen says different things for each.
/// </summary>
internal static class NotInTonightsCrew
{
    private static readonly object Key = new();

    public static void Mark(HttpContext http) => http.Items[Key] = true;

    public static bool WasTheReason(HttpContext http) => http.Items.ContainsKey(Key);
}
