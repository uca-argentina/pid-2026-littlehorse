using DrinkIt.Api.Common;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.VipAccounts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.VipAccounts;

/// <summary>What the menu shows at the top for a VIP table: its number and what it has left to spend.</summary>
public sealed record VipTableAccessResponse(int TableNumber, decimal Balance);

internal static class VipAccountsEndpoints
{
    public static IEndpointRouteBuilder MapVipAccounts(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            // The customer's door, like the menu and the orders: the venue
            // comes from the slug in the QR, and there is no token to carry it.
            // The code goes in the path as the tracking token does, and for the
            // same reason it is never logged by a query string.
            .MapGet("/{venueSlug}/vip-accounts/{code}", FindAsync)
            .AllowAnonymous()
            .WithMetadata(new ScopedBySlugAttribute())
            .WithName("FindVipTable")
            .WithTags("VipAccounts")
            .WithSummary("The VIP table a code opens tonight, with what it has left to spend.")
            .Produces<VipTableAccessResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static async Task<IResult> FindAsync(
        string venueSlug,
        string code,
        CurrentVenue venue,
        IVipAccountQueries accounts,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (venue.Identity is null) return NotFound(ProblemTypes.For("venue.not_found"), $"No venue is reached at '{venueSlug}'.");

        VipTableAccess? access = await accounts.FindOpenAsync(code, clock.GetUtcNow(), cancellationToken);

        return access is null
            ? NotFound(ProblemTypes.For(VipAccountErrors.NotFound.Code), VipAccountErrors.NotFound.Message)
            : TypedResults.Ok(new VipTableAccessResponse(access.TableNumber, access.Balance));
    }

    private static ProblemHttpResult NotFound(string type, string detail) =>
        TypedResults.Problem(title: "Not found", detail: detail, statusCode: StatusCodes.Status404NotFound, type: type);
}
