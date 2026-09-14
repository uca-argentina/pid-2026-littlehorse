using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Staff;

/// <summary>What the administration screen posts. The role travels as its name.</summary>
public sealed record CreateStaffUserRequest(string Username, string Password, string Role);

public sealed record StaffUserResponse(Guid Id, string Username, string Role, bool IsActive);

internal static class StaffUsersEndpoints
{
    public static IEndpointRouteBuilder MapStaffUsers(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints
            // No venue slug in the path, unlike login. Staff take their venue
            // from the token claim, so putting it in the URL would be offering
            // a knob that must never be turned. CLAUDE.md, multi-tenancy.
            .MapGroup("/staff/users")
            .RequireAuthorization(Policies.Administrator)
            .WithTags("Staff users");

        group
            .MapGet("/", ListAsync)
            .WithName("ListStaffUsers")
            .WithSummary("Lists everyone working at the venue, deactivated staff included.")
            .Produces<IReadOnlyList<StaffUserResponse>>();

        group
            .MapPost("/", CreateAsync)
            .WithName("CreateStaffUser")
            .WithSummary("Adds someone to the venue with a role.")
            .Produces<StaffUserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    internal static async Task<IResult> ListAsync(IStaffUserQueries staffUsers, CancellationToken cancellationToken)
    {
        IReadOnlyList<StaffUserListItem> everyone = await staffUsers.ListAsync(cancellationToken);

        return TypedResults.Ok(everyone
            .Select(user => new StaffUserResponse(user.Id, user.Username, user.Role.ToString(), user.IsActive))
            .ToArray());
    }

    internal static async Task<IResult> CreateAsync(
        CreateStaffUserRequest request,
        CreateStaffUserHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryReadRole(request.Role, out StaffRole role)) return RoleIsNotOneWeHandOut(request.Role);

        Result<CreatedStaffUser> result = await handler.HandleAsync(
            new CreateStaffUserCommand(request.Username, request.Password, role),
            cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        CreatedStaffUser created = result.Value;

        // No Location header: there is no endpoint that serves one user on its
        // own, and pointing at the collection would be a lie about what the URI
        // identifies.
        return TypedResults.Created(
            (string?)null,
            new StaffUserResponse(created.Id, created.Username, created.Role.ToString(), created.IsActive));
    }

    /// <summary>
    /// One of our names, and nothing else. Enum.TryParse on its own also accepts
    /// "2", "99", and a comma-separated list that it ORs together even for an
    /// enum that is not [Flags] — "Administrator,Kds" would quietly become a
    /// Waiter. Checking the name against the declared ones first rules out all
    /// three shapes at once.
    /// </summary>
    private static bool TryReadRole(string? name, out StaffRole role)
    {
        role = default;

        if (name is null) return false;
        if (!Enum.GetNames<StaffRole>().Contains(name, StringComparer.OrdinalIgnoreCase)) return false;

        return Enum.TryParse(name, ignoreCase: true, out role);
    }

    private static ProblemHttpResult RoleIsNotOneWeHandOut(string? name) =>
        TypedResults.Problem(
            title: "Invalid request",
            detail: $"'{name}' is not a role this venue can hand out.",
            statusCode: StatusCodes.Status400BadRequest,
            type: ProblemTypes.For(StaffUser.ErrorCodes.RoleInvalid));

    /// <summary>
    /// An expected failure of the use case. A taken username is a conflict with
    /// the current state of the venue, not malformed input, so it is a 409 and
    /// the screen can tell the two apart without reading the message.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error) =>
        TypedResults.Problem(
            title: error == CreateStaffUserHandler.UsernameTaken ? "Username already taken" : "Invalid request",
            detail: error.Message,
            statusCode: error == CreateStaffUserHandler.UsernameTaken
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest,
            type: ProblemTypes.For(error.Code));
}
