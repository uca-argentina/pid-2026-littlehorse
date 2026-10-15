using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Tables;

/// <summary>What the administration screen posts to add a table.</summary>
public sealed record CreateTableRequest(int Number, int Capacity, string? Location);

/// <summary>What the edit screen puts. No number: it is what the orders placed from the table say.</summary>
public sealed record UpdateTableRequest(int Capacity, string? Location);

public sealed record TableResponse(Guid Id, int Number, int Capacity, string? Location, bool IsActive, AuditResponse Audit);

internal static class TablesEndpoints
{
    public static IEndpointRouteBuilder MapTables(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints
            // No venue slug in the path: staff take their venue from the token
            // claim, so putting it in the URL would be offering a knob that
            // must never be turned. CLAUDE.md, multi-tenancy.
            .MapGroup("/staff/tables")
            .RequireAuthorization(Policies.Administrator)
            .WithTags("Tables");

        group
            .MapGet("/", ListAsync)
            .WithName("ListTables")
            .WithSummary("Lists the venue's VIP tables by number, the ones put away included.")
            .Produces<IReadOnlyList<TableResponse>>();

        group
            .MapPost("/", CreateAsync)
            .WithName("CreateTable")
            .WithSummary("Adds a VIP table with its number, how many people it seats and where it is.")
            .Produces<TableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateTable")
            .WithSummary("Changes how many people a table seats and where it is. The number stays.")
            .Produces<TableResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST and not DELETE: nothing is deleted. The row stays so the orders
        // placed from that table keep saying "Mesa 5", and it comes back with
        // the number it always had.
        group
            .MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateTable")
            .WithSummary("Puts a table away without deleting it, unless a night that has not ended sells it.")
            .Produces<TableResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPost("/{id:guid}/reactivate", ReactivateAsync)
            .WithName("ReactivateTable")
            .WithSummary("Brings back a table that was put away.")
            .Produces<TableResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static async Task<IResult> ListAsync(ITableQueries tables, CancellationToken cancellationToken)
    {
        IReadOnlyList<TableSummary> everything = await tables.ListAsync(cancellationToken);

        return TypedResults.Ok(everything.Select(Respond).ToArray());
    }

    internal static async Task<IResult> CreateAsync(
        CreateTableRequest request,
        CreateTableHandler handler,
        CancellationToken cancellationToken)
    {
        // A number or a capacity out of range is not caught here: the domain throws and the
        // global handler turns it into a 400 with the rule's own problem type.
        Result<TableSummary> result = await handler.HandleAsync(new CreateTableCommand(request.Number, request.Capacity, request.Location), cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        // No Location header: there is no endpoint that serves one table on its
        // own, and pointing at the collection would be a lie about what the URI
        // identifies.
        return TypedResults.Created((string?)null, Respond(result.Value));
    }

    internal static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateTableRequest request,
        UpdateTableHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(new UpdateTableCommand(id, request.Capacity, request.Location), cancellationToken));

    internal static async Task<IResult> DeactivateAsync(
        Guid id,
        DeactivateTableHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, cancellationToken));

    internal static async Task<IResult> ReactivateAsync(
        Guid id,
        ReactivateTableHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, cancellationToken));

    /// <summary>The updated table, or the failure named so the screen can branch on it.</summary>
    private static IResult Answer(Result<TableSummary> result) =>
        result.IsSuccess ? TypedResults.Ok(Respond(result.Value)) : Rejected(result.Error!);

    private static TableResponse Respond(TableSummary table) =>
        new(table.Id, table.Number, table.Capacity, table.Location, table.IsActive, AuditResponse.Of(table.Audit));

    /// <summary>
    /// A taken number is a conflict with the venue's tables, and a missing one a
    /// 404; the screen tells both apart from a malformed request without
    /// reading the message.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error)
    {
        (string title, int status) = error switch
        {
            _ when error == TableErrors.NumberTaken => ("Table number already taken", StatusCodes.Status409Conflict),
            _ when error == TableErrors.NotFound => ("Not found", StatusCodes.Status404NotFound),
            _ when error == TableErrors.InANightNotOver => ("Table in a night that has not ended", StatusCodes.Status409Conflict),
            _ => ("Invalid request", StatusCodes.Status400BadRequest),
        };

        return TypedResults.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            type: ProblemTypes.For(error.Code));
    }
}
