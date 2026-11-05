using DrinkIt.Api.Common;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.StampCards;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DrinkIt.Api.Features.StampCards;

// What the administration screen sends to set the card up (US-57).
public sealed record StampCardRuleRequest(int StampsForReward, Guid RewardProductId, bool IsActive);

public sealed record StampCardRuleResponse(int StampsForReward, Guid RewardProductId, bool IsActive, AuditResponse Audit);

// The card as the phone shows it (US-58). When the venue's card is off,
// IsActive is false and the screen draws nothing.
public sealed record StampCardResponse(
    bool IsActive,
    int StampsForReward,
    Guid? RewardProductId,
    string? RewardName,
    int Stamps,
    bool CanRedeem);

internal static class StampCardEndpoints
{
    /// <summary>
    /// The header the phone's token travels in. Never a query string: those are
    /// what proxies and logs keep, and the token is the whole identity of the card.
    /// </summary>
    internal const string TokenHeader = "X-Stamp-Card";

    public static IEndpointRouteBuilder MapStampCards(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder staff = endpoints
            // No venue slug in the path: staff take their venue from the token claim.
            .MapGroup("/staff/stamp-card")
            .RequireAuthorization(Policies.Administrator)
            .WithTags("StampCards");

        staff
            .MapGet("/", GetRuleAsync)
            .WithName("GetStampCardRule")
            .WithSummary("The venue's stamp card rule, or nothing when it was never set up.")
            .Produces<StampCardRuleResponse>()
            .Produces(StatusCodes.Status204NoContent);

        staff
            .MapPut("/", SaveRuleAsync)
            .WithName("SaveStampCardRule")
            .WithSummary("Sets the card up, or replaces its rule. Customers keep their stamps.")
            .Produces<StampCardRuleResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints
            .MapGet("/{venueSlug}/stamp-card", GetCardAsync)
            .AllowAnonymous()
            .WithMetadata(new ScopedBySlugAttribute())
            .WithName("GetStampCard")
            .WithTags("StampCards")
            .WithSummary("The customer's card: the rule and the stamps of the token in the X-Stamp-Card header.")
            .Produces<StampCardResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static async Task<IResult> GetRuleAsync(IStampCardQueries cards, CancellationToken cancellationToken)
    {
        var rule = await cards.GetRuleAsync(cancellationToken);

        if (rule is null) return TypedResults.NoContent();

        return TypedResults.Ok(Shown(StampCardRuleSummary.Of(rule)));
    }

    internal static async Task<IResult> SaveRuleAsync(
        StampCardRuleRequest request,
        SaveStampCardRuleHandler handler,
        CancellationToken cancellationToken)
    {
        // A broken invariant (fewer than two stamps) is not caught here: the
        // domain throws and the global handler answers a 400 with its own code.
        Result<StampCardRuleSummary> result = await handler.HandleAsync(
            new StampCardRuleDefinition(request.StampsForReward, request.RewardProductId, request.IsActive),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(Shown(result.Value)) : Rejected(result.Error!);
    }

    internal static async Task<IResult> GetCardAsync(
        string venueSlug,
        CurrentVenue venue,
        [FromHeader(Name = TokenHeader)] string? token,
        GetStampCardHandler handler,
        IUnderwayNightLookup nights,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (venue.Identity is null) return NoSuchVenue(venueSlug);

        Guid? tonight = await nights.FindIdAsync(clock.GetUtcNow(), cancellationToken);
        StampCardView view = await handler.HandleAsync(token, tonight, cancellationToken);

        return TypedResults.Ok(new StampCardResponse(
            view.IsActive,
            view.StampsForReward,
            view.RewardProductId,
            view.RewardName,
            view.Stamps,
            view.CanRedeem));
    }

    private static StampCardRuleResponse Shown(StampCardRuleSummary rule) =>
        new(rule.StampsForReward, rule.RewardProductId, rule.IsActive, AuditResponse.Of(rule.Audit));

    /// <summary>A reward that is not the venue's product is not found (404); a combo is a bad request.</summary>
    private static ProblemHttpResult Rejected(Error error)
    {
        (string title, int status) = error == StampCardErrors.RewardProductNotFound
            ? ("Product not found", StatusCodes.Status404NotFound)
            : ("Invalid request", StatusCodes.Status400BadRequest);

        return TypedResults.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            type: ProblemTypes.For(error.Code));
    }

    private static ProblemHttpResult NoSuchVenue(string slug) =>
        TypedResults.Problem(
            title: "Not found",
            detail: $"No venue is reached at '{slug}'.",
            statusCode: StatusCodes.Status404NotFound,
            type: ProblemTypes.For("venue.not_found"));
}
