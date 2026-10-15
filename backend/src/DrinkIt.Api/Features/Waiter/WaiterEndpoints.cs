using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Waiter;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Waiter;

/// <summary>One drink on a table's order, as the waiter's phone reads it.</summary>
public sealed record WaiterOrderItemResponse(string ProductName, int Quantity, string? Note);

/// <summary>
/// One table's order that is ready to be carried. <c>TakenByName</c> is null
/// while nobody has it; <c>TakenByMe</c> says whether it is the caller.
/// </summary>
public sealed record WaiterTableResponse(
    Guid OrderId,
    int TableNumber,
    string CustomerName,
    DateTimeOffset? ReadyAt,
    string? TakenByName,
    bool TakenByMe,
    DateTimeOffset? TakenAt,
    IReadOnlyList<WaiterOrderItemResponse> Items);

/// <summary>What the phone gets back once the order is the waiter's.</summary>
public sealed record TakenTableResponse(Guid OrderId, int TableNumber, string WaiterName, DateTimeOffset TakenAt);

internal static class WaiterEndpoints
{
    public static IEndpointRouteBuilder MapWaiter(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints
            // No venue slug in the path, like every other staff route: the
            // waiter takes its venue from the token claim. CLAUDE.md,
            // multi-tenancy.
            .MapGroup("/waiter/tables")
            .RequireAuthorization(Policies.Waiter)
            .WithTags("Waiter");

        group
            .MapGet("/", ListAsync)
            .WithName("ListWaiterTables")
            .WithSummary("The venue's table orders that are ready, the one that has waited longest first.")
            .Produces<IReadOnlyList<WaiterTableResponse>>();

        group
            .MapPost("/{orderId:guid}/take", TakeAsync)
            .WithName("TakeTableOrder")
            .WithSummary("Takes a table's ready order to carry it. Taking it again changes nothing.")
            .Produces<TakenTableResponse>()
            // Not ready any more, or not a table's: a broken transition,
            // answered by the global exception handler.
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group
            .MapPost("/{orderId:guid}/deliver", DeliverAsync)
            .WithName("DeliverTableOrder")
            .WithSummary("Hands over, at its table, an order the caller took. Delivering it again changes nothing.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    internal static async Task<IResult> ListAsync(IWaiterQueries tables, ICurrentStaffUser waiter, CancellationToken cancellationToken)
    {
        IReadOnlyList<WaiterTableOrder> ready = await tables.ListAsync(cancellationToken);

        // "Mine" is decided here, from the token: the phone never learns
        // another waiter's id, and sending its own would be a knob.
        return TypedResults.Ok<IReadOnlyList<WaiterTableResponse>>(
            [.. ready.Select(order => new WaiterTableResponse(
                order.OrderId,
                order.TableNumber,
                order.CustomerName,
                order.ReadyAt,
                order.WaiterName,
                order.WaiterId is not null && order.WaiterId == waiter.Id,
                order.TakenAt,
                [.. order.Items.Select(item => new WaiterOrderItemResponse(item.ProductName, item.Quantity, item.Note))]))]);
    }

    internal static async Task<IResult> TakeAsync(Guid orderId, TakeTableOrderHandler handler, CancellationToken cancellationToken)
    {
        // A move the order cannot make (it is not ready any more) throws from
        // the domain, and the global handler turns it into a 400.
        Result<TakenTableOrder> result = await handler.HandleAsync(orderId, cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        TakenTableOrder taken = result.Value;

        return TypedResults.Ok(new TakenTableResponse(taken.OrderId, taken.TableNumber, taken.WaiterName, taken.TakenAt));
    }

    internal static async Task<IResult> DeliverAsync(Guid orderId, DeliverTableOrderHandler handler, CancellationToken cancellationToken)
    {
        // An order that is no longer ready throws from the domain and the global
        // handler turns it into a 400, as ever.
        Result<DeliveredTableOrder> result = await handler.HandleAsync(orderId, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : Rejected(result.Error!);
    }

    /// <summary>
    /// An order that is no table's of this venue is a 404. Another waiter
    /// having it, or having saved it first, is a conflict: the order is there,
    /// it just is not free, and the list reloads with whose it is.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error)
    {
        bool conflict = error.Code == WaiterErrors.AlreadyTaken.Code
            || error.Code == WaiterErrors.NotYours.Code
            || error.Code == OrderErrors.ChangedMeanwhile.Code;

        return TypedResults.Problem(
            title: conflict ? "Conflict" : "Not found",
            detail: error.Message,
            statusCode: conflict ? StatusCodes.Status409Conflict : StatusCodes.Status404NotFound,
            type: ProblemTypes.For(error.Code));
    }
}
