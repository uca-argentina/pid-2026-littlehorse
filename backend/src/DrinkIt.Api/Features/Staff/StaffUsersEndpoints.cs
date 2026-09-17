using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Staff;

/// <summary>What the administration screen posts. The role travels as its name.</summary>
public sealed record CreateStaffUserRequest(string Username, string Password, string Role);

/// <summary>What the administration screen sends to correct somebody's role.</summary>
public sealed record ChangeStaffUserRoleRequest(string Role);

/// <summary>What it sends to hand somebody a new password.</summary>
public sealed record ResetStaffUserPasswordRequest(string Password);

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

        group
            .MapPut("/{id:guid}/role", ChangeRoleAsync)
            .WithName("ChangeStaffUserRole")
            .WithSummary("Corrects the role somebody was given.")
            .Produces<StaffUserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPut("/{id:guid}/password", ResetPasswordAsync)
            .WithName("ResetStaffUserPassword")
            .WithSummary("Replaces a forgotten password with a new one.")
            .Produces<StaffUserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST and not DELETE: nothing is deleted. The row stays so the orders
        // that person prepared keep pointing at their account, and the same
        // account comes back when they do.
        group
            .MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateStaffUser")
            .WithSummary("Takes away access without taking away the account.")
            .Produces<StaffUserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPost("/{id:guid}/reactivate", ReactivateAsync)
            .WithName("ReactivateStaffUser")
            .WithSummary("Gives somebody who came back the account they always had.")
            .Produces<StaffUserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    internal static async Task<IResult> ChangeRoleAsync(
        Guid id,
        ChangeStaffUserRoleRequest request,
        ChangeStaffUserRoleHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryReadRole(request.Role, out StaffRole role)) return RoleIsNotOneWeHandOut(request.Role);

        return Answer(await handler.HandleAsync(new ChangeStaffUserRoleCommand(id, role), cancellationToken));
    }

    internal static async Task<IResult> ResetPasswordAsync(
        Guid id,
        ResetStaffUserPasswordRequest request,
        ResetStaffUserPasswordHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(
            new ResetStaffUserPasswordCommand(id, request.Password), cancellationToken));

    internal static async Task<IResult> DeactivateAsync(
        Guid id,
        DeactivateStaffUserHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, cancellationToken));

    internal static async Task<IResult> ReactivateAsync(
        Guid id,
        ReactivateStaffUserHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, cancellationToken));

    /// <summary>The updated user, or the failure named so the screen can branch on it.</summary>
    private static IResult Answer(Result<StaffUserSummary> result)
    {
        if (!result.IsSuccess) return Rejected(result.Error!);

        StaffUserSummary user = result.Value;

        return TypedResults.Ok(
            new StaffUserResponse(user.Id, user.Username, user.Role.ToString(), user.IsActive));
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
    /// What each expected failure means over HTTP. A conflict is a request that
    /// argues with the state of the venue — the username is taken, or that is
    /// the last administrator left — as opposed to one that is simply
    /// malformed, and the screen tells them apart without reading the message.
    /// </summary>
    private static readonly Dictionary<string, int> StatusByErrorCode = new(StringComparer.Ordinal)
    {
        [StaffUserErrors.NotFound.Code] = StatusCodes.Status404NotFound,
        [CreateStaffUserHandler.UsernameTaken.Code] = StatusCodes.Status409Conflict,
        [StaffUserErrors.LastAdministrator.Code] = StatusCodes.Status409Conflict,
    };

    private static readonly Dictionary<int, string> TitleByStatus = new()
    {
        [StatusCodes.Status404NotFound] = "Not found",
        [StatusCodes.Status409Conflict] = "Conflict with the current state",
    };

    private static ProblemHttpResult Rejected(Error error)
    {
        // Anything the table does not name is malformed input, which is the
        // only other shape an expected failure of these use cases can take.
        int status = StatusByErrorCode.GetValueOrDefault(error.Code, StatusCodes.Status400BadRequest);

        return TypedResults.Problem(
            title: TitleByStatus.GetValueOrDefault(status, "Invalid request"),
            detail: error.Message,
            statusCode: status,
            type: ProblemTypes.For(error.Code));
    }
}
