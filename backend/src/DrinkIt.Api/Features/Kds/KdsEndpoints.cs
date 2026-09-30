using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Kds;

/// <summary>One drink on a card of the board, as the bar's tablet reads it.</summary>
public sealed record KdsQueueOrderItemResponse(string ProductName, int Quantity, string? Note);

/// <summary>One order on the bar's board: who asked, what for, since when, and where it is.</summary>
public sealed record KdsQueueOrderResponse(
    string Code,
    string CustomerName,
    KdsOrderStatus Status,
    DateTimeOffset PaidAt,
    DateTimeOffset? LastModifiedAt,
    bool IsForTable,
    IReadOnlyList<KdsQueueOrderItemResponse> OrderItems);

/// <summary>What the bar's camera or reader read off the customer's phone.</summary>
public sealed record ScanRequest(string? Read);

/// <summary>Whose order a scan found, and what it did with it.</summary>
public sealed record ScannedOrderResponse(string Code, string CustomerName, ScanOutcomeName Outcome);

internal static class KdsEndpoints
{
    public static IEndpointRouteBuilder MapKds(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            // No venue slug in the path, like every other staff route: the
            // station takes its venue from the token claim. CLAUDE.md,
            // multi-tenancy.
            .MapGet("/kds/queue", GetQueueAsync)
            .RequireAuthorization(Policies.Kds)
            .WithName("GetKdsQueue")
            .WithTags("Kds")
            .WithSummary("The bar's queue: every paid order still on its way, oldest paid first.")
            .Produces<IReadOnlyList<KdsQueueOrderResponse>>();

        endpoints
            .MapPost("/kds/scan", ScanAsync)
            .RequireAuthorization(Policies.Kds)
            .WithName("ScanOrder")
            .WithTags("Kds")
            .WithSummary("Hands over the ready order a customer's QR leads to, or says why it did not.")
            .Produces<ScannedOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        RouteGroupBuilder orders = endpoints
            .MapGroup("/kds/orders/{code}")
            .RequireAuthorization(Policies.Kds)
            .WithTags("Kds");

        orders
            .MapPost("/start-preparing", StartPreparingAsync)
            .WithName("StartPreparingOrder")
            .WithSummary("Takes a queued order into preparation. Taking it again changes nothing.")
            .Produces(StatusCodes.Status204NoContent)
            // An order no longer where the board thought it was: a broken
            // transition, answered by the global exception handler.
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders
            .MapPost("/return-to-queue", ReturnToQueueAsync)
            .WithName("ReturnOrderToQueue")
            .WithSummary("Hands an order in preparation back to the queue, keeping when it was paid.")
            .Produces(StatusCodes.Status204NoContent)
            // An order no longer where the board thought it was: a broken
            // transition, answered by the global exception handler.
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders
            .MapPost("/mark-ready", MarkReadyAsync)
            .WithName("MarkOrderReady")
            .WithSummary("Marks an order in preparation ready. Marking it again changes nothing.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders
            .MapPost("/return-to-preparation", ReturnToPreparationAsync)
            .WithName("ReturnOrderToPreparation")
            .WithSummary("Sends an order marked ready by mistake back to preparation.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders
            .MapPost("/deliver", DeliverAsync)
            .WithName("DeliverOrder")
            .WithSummary("Hands a ready order over at the bar, when it cannot be scanned. Delivering it again changes nothing.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders
            .MapPost("/undo-delivery", UndoDeliveryAsync)
            .WithName("UndoOrderDelivery")
            .WithSummary("Undoes a delivery made moments ago; the order is ready again. Refused once the grace has passed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    internal static async Task<IResult> StartPreparingAsync(
        string code,
        StartPreparingHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(code, cancellationToken));

    internal static async Task<IResult> ReturnToQueueAsync(
        string code,
        ReturnToQueueHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(code, cancellationToken));

    internal static async Task<IResult> MarkReadyAsync(
        string code,
        MarkReadyHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(code, cancellationToken));

    internal static async Task<IResult> ReturnToPreparationAsync(
        string code,
        ReturnToPreparationHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(code, cancellationToken));

    internal static async Task<IResult> DeliverAsync(
        string code,
        DeliverHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(code, cancellationToken));

    internal static async Task<IResult> UndoDeliveryAsync(
        string code,
        UndoDeliveryHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(code, cancellationToken));

    /// <summary>
    /// "Not ready yet" and "already delivered" are answers, not failures: the
    /// screen still needs whose order it was to say so.
    /// </summary>
    internal static async Task<IResult> ScanAsync(
        ScanRequest request,
        ScanHandler handler,
        CancellationToken cancellationToken)
    {
        Result<ScannedOrder> result = await handler.HandleAsync(request.Read, cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        ScannedOrder scanned = result.Value;

        return TypedResults.Ok(new ScannedOrderResponse(scanned.Code, scanned.CustomerName, scanned.Outcome.ToContract()));
    }

    /// <summary>
    /// Nothing to hand back on success: the board reloads its queue when the
    /// hub says it changed, which is also how every other tablet finds out.
    /// </summary>
    private static IResult Answer(Result<OrderStatus> result)
    {
        if (result.IsSuccess) return TypedResults.NoContent();

        return Rejected(result.Error!);
    }

    /// <summary>
    /// A code that is no order of this venue is a 404. Another tablet moving
    /// the order in the same instant is a conflict: the order is there, it
    /// just is not where this tablet thought, and the board reloads with it.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error)
    {
        bool changedMeanwhile = error.Code == OrderErrors.ChangedMeanwhile.Code;

        return TypedResults.Problem(
            title: changedMeanwhile ? "Conflict" : "Not found",
            detail: error.Message,
            statusCode: changedMeanwhile ? StatusCodes.Status409Conflict : StatusCodes.Status404NotFound,
            type: ProblemTypes.For(error.Code));
    }

    internal static async Task<IResult> GetQueueAsync(IKdsQueueQueries queue, CancellationToken cancellationToken)
    {
        IReadOnlyList<KdsQueueOrder> orders = await queue.GetQueueAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<KdsQueueOrderResponse>>(
            [.. orders.Select(order => new KdsQueueOrderResponse(
                order.Code,
                order.CustomerName,
                order.Status.ToKdsStatus(),
                order.PaidAt,
                order.LastModifiedAt,
                order.IsForTable,
                [.. order.OrderItems.Select(item => new KdsQueueOrderItemResponse(item.ProductName, item.Quantity, item.Note))]))]);
    }
}
