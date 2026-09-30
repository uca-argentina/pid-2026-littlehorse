using DrinkIt.Api.Common;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Cashier;

/// <summary>One drink of an order, as the till reads it back.</summary>
public sealed record CashierOrderItemResponse(string ProductName, int Quantity, string? Note, decimal UnitPrice);

/// <summary>An order as the till shows it: who, what, and how much to charge.</summary>
public sealed record CashierOrderResponse(
    string Code,
    string CustomerName,
    CustomerOrderStatus Status,
    decimal Total,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? PaidAt,
    IReadOnlyList<CashierOrderItemResponse> Items);

/// <summary>What a reader or the camera read off the customer's phone.</summary>
public sealed record CashierScanRequest(string? Read);

internal static class CashierEndpoints
{
    internal static async Task<IResult> ScanAsync(CashierScanRequest request, ICashierQueries queries, CancellationToken cancellationToken) =>
        await queries.FindByTokenAsync(request.Read ?? string.Empty, cancellationToken) is CashierOrder order
            ? TypedResults.Ok(ToResponse(order))
            : Rejected(CashierErrors.OrderNotFound);

    internal static async Task<IResult> GetMyCollectionsAsync(MyCollectionsHandler handler, CancellationToken cancellationToken)
    {
        IReadOnlyList<CashierOrder> orders = await handler.HandleAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<CashierOrderResponse>>([.. orders.Select(ToResponse)]);
    }

    public static IEndpointRouteBuilder MapCashier(this IEndpointRouteBuilder endpoints)
    {
        // No venue slug in the path, like every other staff route: the till
        // takes its venue from the token claim. CLAUDE.md, multi-tenancy.
        RouteGroupBuilder orders = endpoints
            .MapGroup("/cashier/orders")
            .RequireAuthorization(Policies.Cashier)
            .WithTags("Cashier");

        orders
            .MapGet("/", GetAwaitingPaymentAsync)
            .WithName("GetOrdersAwaitingPayment")
            .WithSummary("Every order waiting to be paid in cash, the one that has waited longest first.")
            .Produces<IReadOnlyList<CashierOrderResponse>>();

        orders
            .MapGet("/{code}", FindAsync)
            .WithName("FindOrderAtTheTill")
            .WithSummary("The order behind a code, whatever its state, so the till can tell unpaid from already paid.")
            .Produces<CashierOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders
            .MapPost("/{code}/collect", CollectAsync)
            .WithName("CollectCashPayment")
            .WithSummary("Records that the till took the money, and hands the order to the bar.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders
            .MapPost("/{code}/cancel", CancelAsync)
            .WithName("CancelAtTheTill")
            .WithSummary("Cancels an order still waiting to be paid in cash, and puts its drinks back on the shelf.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints
            // POST with the token in the body, like the bar's scan: the token is
            // the customer's proof, and a URL ends up in access logs.
            .MapPost("/cashier/scan", ScanAsync)
            .RequireAuthorization(Policies.Cashier)
            .WithTags("Cashier")
            .WithName("ScanAtTheTill")
            .WithSummary("The order behind the QR on the customer's phone, whatever its state.")
            .Produces<CashierOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints
            .MapGet("/cashier/collections", GetMyCollectionsAsync)
            .RequireAuthorization(Policies.Cashier)
            .WithTags("Cashier")
            .WithName("GetMyCollections")
            .WithSummary("What the signed-in cashier collected during the shift, the latest first.")
            .Produces<IReadOnlyList<CashierOrderResponse>>();

        return endpoints;
    }

    internal static async Task<IResult> GetAwaitingPaymentAsync(ICashierQueries queries, CancellationToken cancellationToken)
    {
        IReadOnlyList<CashierOrder> orders = await queries.GetAwaitingPaymentAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<CashierOrderResponse>>([.. orders.Select(ToResponse)]);
    }

    internal static async Task<IResult> FindAsync(string code, ICashierQueries queries, CancellationToken cancellationToken) =>
        await queries.FindAsync(code, cancellationToken) is CashierOrder order
            ? TypedResults.Ok(ToResponse(order))
            : Rejected(CashierErrors.OrderNotFound);

    /// <summary>
    /// Nothing to hand back on success: the screen goes back to the list, and
    /// the bar hears about it through its hub like any other queued order.
    /// </summary>
    internal static async Task<IResult> CollectAsync(string code, CollectCashHandler handler, CancellationToken cancellationToken)
    {
        Result<OrderStatus> result = await handler.HandleAsync(code, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : Rejected(result.Error!);
    }

    // US-23: the customer left without paying. Nothing to hand back either:
    // the order leaves "Por cobrar" through the till's hub.
    internal static async Task<IResult> CancelAsync(string code, CancelAtTillHandler handler, CancellationToken cancellationToken)
    {
        Result<OrderStatus> result = await handler.HandleAsync(code, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : Rejected(result.Error!);
    }

    private static ProblemHttpResult Rejected(Error error)
    {
        bool alreadyPaid = error.Code == CashierErrors.AlreadyPaid.Code;

        return TypedResults.Problem(
            title: alreadyPaid ? "Conflict" : "Not found",
            detail: error.Message,
            statusCode: alreadyPaid ? StatusCodes.Status409Conflict : StatusCodes.Status404NotFound,
            type: ProblemTypes.For(error.Code));
    }

    private static CashierOrderResponse ToResponse(CashierOrder order) => new(
        order.Code,
        order.CustomerName,
        order.Status.ToCustomerStatus(),
        order.Total,
        order.PlacedAt,
        order.PaidAt,
        [.. order.Items.Select(item => new CashierOrderItemResponse(item.ProductName, item.Quantity, item.Note, item.UnitPrice))]);
}
