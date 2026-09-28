using DrinkIt.Api.Common;
using DrinkIt.Application.Kds;

namespace DrinkIt.Api.Features.Kds;

/// <summary>One drink on a card of the board, as the bar's tablet reads it.</summary>
public sealed record KdsQueueLineResponse(string ProductName, int Quantity, string? Note);

/// <summary>One card of the bar's board (US-15).</summary>
public sealed record KdsQueueOrderResponse(
    string Code,
    string CustomerName,
    string Status,
    DateTimeOffset PaidAt,
    bool IsForTable,
    IReadOnlyList<KdsQueueLineResponse> Lines);

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

        return endpoints;
    }

    internal static async Task<IResult> GetQueueAsync(IKdsQueueQueries queue, CancellationToken cancellationToken)
    {
        IReadOnlyList<KdsQueueOrder> orders = await queue.GetQueueAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<KdsQueueOrderResponse>>(
            [.. orders.Select(order => new KdsQueueOrderResponse(
                order.Code,
                order.CustomerName,
                order.Status.ToString(),
                order.PaidAt,
                order.IsForTable,
                [.. order.Lines.Select(line => new KdsQueueLineResponse(line.ProductName, line.Quantity, line.Note))]))]);
    }
}
