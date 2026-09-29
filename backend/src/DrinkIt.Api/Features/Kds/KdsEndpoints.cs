using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Orders;

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
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders
            .MapPost("/return-to-queue", ReturnToQueueAsync)
            .WithName("ReturnOrderToQueue")
            .WithSummary("Hands an order in preparation back to the queue, keeping when it was paid.")
            .Produces(StatusCodes.Status204NoContent)
            // An order no longer where the board thought it was: a broken
            // transition, answered by the global exception handler.
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders
            .MapPost("/mark-ready", MarkReadyAsync)
            .WithName("MarkOrderReady")
            .WithSummary("Marks an order in preparation ready. Marking it again changes nothing.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders
            .MapPost("/return-to-preparation", ReturnToPreparationAsync)
            .WithName("ReturnOrderToPreparation")
            .WithSummary("Sends an order marked ready by mistake back to preparation.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders
            .MapPost("/deliver", DeliverAsync)
            .WithName("DeliverOrder")
            .WithSummary("Hands a ready order over at the bar, when it cannot be scanned. Delivering it again changes nothing.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders
            .MapPost("/undo-delivery", UndoDeliveryAsync)
            .WithName("UndoOrderDelivery")
            .WithSummary("Undoes a delivery made moments ago; the order is ready again. Refused once the grace has passed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

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
    /// Nothing to hand back on success: the board reloads its queue when the
    /// hub says it changed, which is also how every other tablet finds out.
    /// </summary>
    private static IResult Answer(Result<OrderStatus> result)
    {
        if (result.IsSuccess) return TypedResults.NoContent();

        // The only expected failure of these use cases.
        return TypedResults.Problem(
            title: "Not found",
            detail: result.Error!.Message,
            statusCode: StatusCodes.Status404NotFound,
            type: ProblemTypes.For(result.Error.Code));
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
