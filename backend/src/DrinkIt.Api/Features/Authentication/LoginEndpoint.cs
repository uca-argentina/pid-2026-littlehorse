using DrinkIt.Api.Common;
using DrinkIt.Application.Authentication;
using DrinkIt.Application.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Authentication;

/// <summary>What the client posts. Kept apart from LoginCommand so the wire
/// contract can change without dragging the use case with it.</summary>
public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, string Username, string Role);

internal static class LoginEndpoint
{
    public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            // The slug is in the route because usernames are only unique within
            // a venue and there is no token yet. Knowing a slug grants nothing:
            // credentials are still required.
            .MapPost("/{venueSlug}/auth/login", HandleAsync)
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Signs a staff member in to one venue.")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    internal static async Task<IResult> HandleAsync(
        LoginRequest request,
        LoginHandler handler,
        CancellationToken cancellationToken)
    {
        Result<StaffSession> result = await handler.HandleAsync(
            new LoginCommand(request.Username, request.Password),
            cancellationToken);

        if (!result.IsSuccess) return Unauthorized(result.Error!);

        StaffSession session = result.Value;

        return TypedResults.Ok(new LoginResponse(
            session.Token,
            session.ExpiresAt,
            session.Username,
            session.Role.ToString()));
    }

    /// <summary>
    /// The error code travels as the problem type so the PWA can branch on
    /// something stable instead of on a message that will get reworded. It goes
    /// through ProblemTypes because RFC 9457 requires a URI there, and a bare
    /// code is a relative one that resolves differently per host.
    /// </summary>
    private static ProblemHttpResult Unauthorized(Error error) =>
        TypedResults.Problem(
            title: "Authentication failed",
            detail: error.Message,
            statusCode: StatusCodes.Status401Unauthorized,
            type: ProblemTypes.For(error.Code));
}
