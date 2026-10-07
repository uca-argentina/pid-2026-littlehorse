using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Nights;

/// <summary>What the administration screen posts to set up a night.</summary>
/// <remarks>US-35.</remarks>
public sealed record CreateNightRequest(string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> CrewIds);

public sealed record NightResponse(
    Guid Id,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<Guid> CrewIds,
    AuditResponse Audit)
{
    internal static NightResponse Of(NightSummary night) =>
        new(night.Id, night.Name, night.StartsAt, night.EndsAt, night.CrewIds, AuditResponse.Of(night.Audit));
}

internal static class NightsEndpoints
{
    public static IEndpointRouteBuilder MapNights(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints
            // The venue comes from the token claim, like every staff endpoint.
            .MapGroup("/nights")
            .RequireAuthorization(Policies.Administrator)
            .WithTags("Nights");

        group
            .MapGet("/", ListAsync)
            .WithName("ListNights")
            .WithSummary("Lists every night of the venue, the latest first.")
            .Produces<IReadOnlyList<NightResponse>>();

        group
            .MapPost("/", CreateAsync)
            .WithName("CreateNight")
            .WithSummary("Sets up a night of the venue with its hours and the staff working it.")
            .Produces<NightResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    internal static async Task<IResult> ListAsync(INightQueries nights, CancellationToken cancellationToken)
    {
        IReadOnlyList<NightSummary> listed = await nights.ListAsync(cancellationToken);

        return TypedResults.Ok(listed.Select(NightResponse.Of).ToArray());
    }

    internal static async Task<IResult> CreateAsync(
        CreateNightRequest request,
        CreateNightHandler handler,
        CancellationToken cancellationToken)
    {
        Result<NightSummary> result = await handler.HandleAsync(
            new CreateNightCommand(request.Name, request.StartsAt, request.EndsAt, request.CrewIds ?? []),
            cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        // No Location header, for the same reason as staff users: nothing
        // serves one night on its own yet.
        return TypedResults.Created((string?)null, NightResponse.Of(result.Value));
    }

    /// <summary>
    /// Overlapping argues with the venue's other nights, so it is a conflict;
    /// anything else expected is malformed input.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error)
    {
        bool conflict = error.Code == NightErrors.Overlaps.Code;

        return TypedResults.Problem(
            title: conflict ? "Conflict with the current state" : "Invalid request",
            detail: error.Message,
            statusCode: conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
            type: ProblemTypes.For(error.Code));
    }
}
