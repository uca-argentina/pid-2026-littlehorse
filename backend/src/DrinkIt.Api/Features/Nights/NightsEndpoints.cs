using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Nights;

/// <summary>What the administration screen posts to set up a night.</summary>
/// <remarks>US-35.</remarks>
public sealed record CreateNightRequest(string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> CrewIds);

/// <summary>What the night's own screen saves: the whole form, as on creation.</summary>
public sealed record UpdateNightRequest(string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> CrewIds);

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
            .MapGet("/{id:guid}", GetAsync)
            .WithName("GetNight")
            .WithSummary("One night of the venue, with its hours and its crew.")
            .Produces<NightResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateNight")
            .WithSummary("Changes a night that has not ended. Once it started, its start stays; an end already past closes it now.")
            .Produces<NightResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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

    internal static async Task<IResult> GetAsync(Guid id, INightQueries nights, CancellationToken cancellationToken)
    {
        NightSummary? night = await nights.GetAsync(id, cancellationToken);

        return night is null ? Rejected(NightErrors.NotFound) : TypedResults.Ok(NightResponse.Of(night));
    }

    internal static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateNightRequest request,
        UpdateNightHandler handler,
        CancellationToken cancellationToken)
    {
        Result<NightSummary> result = await handler.HandleAsync(
            new UpdateNightCommand(id, request.Name, request.StartsAt, request.EndsAt, request.CrewIds ?? []),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(NightResponse.Of(result.Value)) : Rejected(result.Error!);
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
    /// anything the table does not name is malformed input.
    /// </summary>
    private static readonly Dictionary<string, int> StatusByErrorCode = new(StringComparer.Ordinal)
    {
        [NightErrors.NotFound.Code] = StatusCodes.Status404NotFound,
        [NightErrors.Overlaps.Code] = StatusCodes.Status409Conflict,
    };

    private static readonly Dictionary<int, string> TitleByStatus = new()
    {
        [StatusCodes.Status404NotFound] = "Not found",
        [StatusCodes.Status409Conflict] = "Conflict with the current state",
    };

    private static ProblemHttpResult Rejected(Error error)
    {
        int status = StatusByErrorCode.GetValueOrDefault(error.Code, StatusCodes.Status400BadRequest);

        return TypedResults.Problem(
            title: TitleByStatus.GetValueOrDefault(status, "Invalid request"),
            detail: error.Message,
            statusCode: status,
            type: ProblemTypes.For(error.Code));
    }
}
