using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Promotions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Promotions;

/// <summary>
/// What the administration screen posts to define a promotion.
/// </summary>
/// <remarks>
/// <see cref="Days"/> are the days of the week the window starts on, numbered
/// like JavaScript's <c>getDay()</c>: 0 is Sunday, 6 is Saturday. A window that
/// crosses midnight belongs to the day it starts. The times are the venue's own
/// local hours.
/// </remarks>
public sealed record PromotionRequest(
    string Name,
    int DiscountPercent,
    IReadOnlyList<int>? Days,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    IReadOnlyList<Guid>? ProductIds);

public sealed record PromotionResponse(
    Guid Id,
    string Name,
    int DiscountPercent,
    IReadOnlyList<int> Days,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    IReadOnlyList<Guid> ProductIds,
    bool IsActive,
    AuditResponse Audit);

internal static class PromotionsEndpoints
{
    public static IEndpointRouteBuilder MapPromotions(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints
            // No venue slug in the path: staff take their venue from the token
            // claim. CLAUDE.md, multi-tenancy.
            .MapGroup("/staff/promotions")
            .RequireAuthorization(Policies.Administrator)
            .WithTags("Promotions");

        group
            .MapGet("/", ListAsync)
            .WithName("ListPromotions")
            .WithSummary("Lists every promotion of the venue, deactivated ones included.")
            .Produces<IReadOnlyList<PromotionResponse>>();

        group
            .MapPost("/", CreateAsync)
            .WithName("CreatePromotion")
            .WithSummary("Adds a promotion to the venue, active.")
            .Produces<PromotionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdatePromotion")
            .WithSummary("Replaces what a promotion is, for the orders to come.")
            .Produces<PromotionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST and not PUT or DELETE: nothing is deleted, and the state is a
        // switch, the same shape as the products' deactivate.
        group
            .MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivatePromotion")
            .WithSummary("Switches a promotion on.")
            .Produces<PromotionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivatePromotion")
            .WithSummary("Switches a promotion off: new orders stop getting it, confirmed ones keep their price.")
            .Produces<PromotionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static async Task<IResult> ListAsync(IPromotionQueries promotions, CancellationToken cancellationToken)
    {
        IReadOnlyList<PromotionListItem> everything = await promotions.ListAsync(cancellationToken);

        return TypedResults.Ok(everything
            .Select(item => new PromotionResponse(
                item.Id,
                item.Name,
                item.DiscountPercent,
                [.. item.Days.Select(day => (int)day)],
                item.StartsAt,
                item.EndsAt,
                item.ProductIds,
                item.IsActive,
                AuditResponse.Of(item.Audit)))
            .ToArray());
    }

    internal static async Task<IResult> CreateAsync(
        PromotionRequest request,
        CreatePromotionHandler handler,
        CancellationToken cancellationToken)
    {
        // A broken invariant (percent out of range, no days) is not caught
        // here: the domain throws and the global handler answers a 400 with the
        // rule's own problem type.
        Result<PromotionSummary> result = await handler.HandleAsync(Definition(request), cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        // No Location header: there is no endpoint serving one promotion alone.
        return TypedResults.Created((string?)null, Shown(result.Value));
    }

    internal static async Task<IResult> UpdateAsync(
        Guid id,
        PromotionRequest request,
        UpdatePromotionHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(new UpdatePromotionCommand(id, Definition(request)), cancellationToken));

    internal static async Task<IResult> ActivateAsync(
        Guid id,
        SetPromotionActiveHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, true, cancellationToken));

    internal static async Task<IResult> DeactivateAsync(
        Guid id,
        SetPromotionActiveHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, false, cancellationToken));

    private static PromotionDefinition Definition(PromotionRequest request) => new(
        request.Name,
        request.DiscountPercent,
        [.. (request.Days ?? []).Select(day => (DayOfWeek)day)],
        request.StartsAt,
        request.EndsAt,
        request.ProductIds ?? []);

    private static IResult Answer(Result<PromotionSummary> result) =>
        result.IsSuccess ? TypedResults.Ok(Shown(result.Value)) : Rejected(result.Error!);

    /// <summary>The one place a written promotion becomes the JSON the screen reads.</summary>
    private static PromotionResponse Shown(PromotionSummary promotion) => new(
        promotion.Id,
        promotion.Name,
        promotion.DiscountPercent,
        [.. promotion.Days.Select(day => (int)day)],
        promotion.StartsAt,
        promotion.EndsAt,
        promotion.ProductIds,
        promotion.IsActive,
        AuditResponse.Of(promotion.Audit));

    /// <summary>
    /// A promotion or a product that is not the venue's is not found (404);
    /// anything else is malformed input.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error)
    {
        (string title, int status) = error.Code switch
        {
            _ when error == PromotionErrors.NotFound => ("Promotion not found", StatusCodes.Status404NotFound),
            _ when error == PromotionErrors.ProductNotFound => ("Product not found", StatusCodes.Status404NotFound),
            _ => ("Invalid request", StatusCodes.Status400BadRequest),
        };

        return TypedResults.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            type: ProblemTypes.For(error.Code));
    }
}
