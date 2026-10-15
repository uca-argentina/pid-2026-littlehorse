using DrinkIt.Api.Common;
using DrinkIt.Api.Features.Waiter;
using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Waiter;
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

/// <summary>
/// One table's order that is ready to be carried, as the till sees it: who has
/// it, if anybody, and since when. The till only looks: coordinating is done
/// out loud.
/// </summary>
public sealed record CashierVipDeliveryResponse(
    Guid OrderId,
    int TableNumber,
    string CustomerName,
    DateTimeOffset? ReadyAt,
    string? TakenByName,
    DateTimeOffset? TakenAt,
    IReadOnlyList<WaiterOrderItemResponse> Items);

/// <summary>What a reader or the camera read off the customer's phone.</summary>
public sealed record CashierScanRequest(string? Read);

internal static class CashierEndpoints
{
    /// <summary>
    /// The tables ready to be carried, the one that has waited longest first.
    /// The same list the waiters read, minus the waiter's id: the till only
    /// needs to know who has it, to say it out loud.
    /// </summary>
    internal static async Task<IResult> GetVipDeliveriesAsync(IWaiterQueries tables, CancellationToken cancellationToken)
    {
        IReadOnlyList<WaiterTableOrder> ready = await tables.ListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<CashierVipDeliveryResponse>>(
            [.. ready.Select(order => new CashierVipDeliveryResponse(
                order.OrderId,
                order.TableNumber,
                order.CustomerName,
                order.ReadyAt,
                order.WaiterName,
                order.TakenAt,
                [.. order.Items.Select(item => new WaiterOrderItemResponse(item.ProductName, item.Quantity, item.Note))]))]);
    }

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

        endpoints
            .MapGet("/cashier/vip-deliveries", GetVipDeliveriesAsync)
            .RequireAuthorization(Policies.Cashier)
            .WithTags("Cashier")
            .WithName("GetVipDeliveries")
            .WithSummary("The VIP tables that are ready and who is carrying each, the one that has waited longest first.")
            .Produces<IReadOnlyList<CashierVipDeliveryResponse>>();

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

    private static readonly HashSet<string> ConflictCodes = new(StringComparer.Ordinal)
    {
        CashierErrors.AlreadyPaid.Code,
        CashierErrors.SoldOut.Code,
        CashierErrors.NightOver.Code,
        OrderErrors.StockMoved.Code,
    };

    private static ProblemHttpResult Rejected(Error error)
    {
        // Everything but a code nobody has argues with the order's state: paid
        // already, a drink gone, a race lost for it — try again —, or a night
        // that is over (US-36).
        bool conflict = ConflictCodes.Contains(error.Code);

        // The drink that ran out (US-36), under the same name confirming an
        // order uses, so the till can say which one.
        return TypedResults.Problem(
            title: conflict ? "Conflict" : "Not found",
            detail: error.Message,
            statusCode: conflict ? StatusCodes.Status409Conflict : StatusCodes.Status404NotFound,
            type: ProblemTypes.For(error.Code),
            extensions: error.Subject is null
                ? null
                : new Dictionary<string, object?> { ["productName"] = error.Subject });
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
