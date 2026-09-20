using DrinkIt.Api.Common;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Orders;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Orders;

/// <summary>One drink of the order, as the tracking screen draws it.</summary>
public sealed record TrackedOrderItemResponse(string ProductName, int Quantity, string? Note);

/// <summary>
/// Where an order is.
/// </summary>
/// <remarks>
/// The tracking token is not in here. The screen already holds it — it is in
/// the address it asked with — and this answer is fetched every three seconds,
/// travels to a phone and passes through caches and logs on the way.
/// </remarks>
public sealed record TrackedOrderResponse(
    string Code,
    string CustomerName,
    string Status,
    decimal Total,
    DateTimeOffset? PaidAt,
    IReadOnlyList<TrackedOrderItemResponse> Items);

internal static class OrderTrackingEndpoint
{
    public static IEndpointRouteBuilder MapOrderTracking(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            // The token is a path segment rather than a query string: query
            // strings are what proxies, logs and analytics keep by default, and
            // this one is the only thing standing between a stranger and
            // somebody's order.
            .MapGet("/{venueSlug}/orders/{code}/{token}", FollowAsync)
            .AllowAnonymous()
            .WithMetadata(new ScopedBySlugAttribute())
            .WithName("FollowOrder")
            .WithTags("Orders")
            .WithSummary("Where an order is, for the customer holding its link.")
            .Produces<TrackedOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Answers 404 to every way of not getting in: a wrong token, a code that
    /// belongs to nobody, another venue's order, and an order already handed
    /// over. A 403 would confirm to somebody working through codes that this
    /// one exists, which is the half of the answer worth hiding.
    /// </summary>
    internal static async Task<IResult> FollowAsync(
        string venueSlug,
        string code,
        string token,
        CurrentVenue venue,
        IOrderTrackingQueries orders,
        CancellationToken cancellationToken)
    {
        if (venue.Identity is null) return NoSuchOrder();

        TrackedOrder? found = await orders.FindAsync(code, token, cancellationToken);

        if (found is null) return NoSuchOrder();

        return TypedResults.Ok(new TrackedOrderResponse(
            found.Code,
            found.CustomerName,
            found.Status.ToString(),
            found.Total,
            found.PaidAt,
            [.. found.Items.Select(item =>
                new TrackedOrderItemResponse(item.ProductName, item.Quantity, item.Note))]));
    }

    private static ProblemHttpResult NoSuchOrder() =>
        TypedResults.Problem(
            title: "Not found",
            detail: "That link does not lead to an order.",
            statusCode: StatusCodes.Status404NotFound,
            type: ProblemTypes.For("order.not_found"));
}
