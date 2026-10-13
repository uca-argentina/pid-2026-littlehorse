using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
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

/// <summary>
/// How much a product's stock moves in one bar for the night: positive when
/// units go in from the storeroom, negative when they go back to it. A change,
/// never the new total, so a sale made while the screen was open is not
/// overwritten.
/// </summary>
/// <remarks>US-37.</remarks>
public sealed record AdjustNightStockRequest(int Change);

/// <summary>
/// One product in one bar for the night. Every night's bars start empty, so
/// Loaded is all that came in. InStoreroom is the product's, the same in every
/// bar: what is left to load.
/// </summary>
public sealed record NightStockLineResponse(Guid ProductId, string ProductName, int Loaded, int Sold, int Left, int InStoreroom);

/// <summary>One bar of the night: a KDS of its crew, with its own stock.</summary>
public sealed record NightStockBarResponse(Guid KdsId, string Name, IReadOnlyList<NightStockLineResponse> Lines);

public sealed record NightStockResponse(Guid NightId, IReadOnlyList<NightStockBarResponse> Bars)
{
    internal static NightStockResponse Of(NightStockSheet sheet) => new(
        sheet.NightId,
        [.. sheet.Bars.Select(bar => new NightStockBarResponse(
            bar.KdsId,
            bar.Name,
            [.. bar.Lines.Select(line => new NightStockLineResponse(line.ProductId, line.ProductName, line.Loaded, line.Sold, line.Left, line.InStoreroom))]))]);
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
            .MapGet("/{id:guid}/stock", GetStockAsync)
            .WithName("GetNightStock")
            .WithSummary("The night's stock, bar by bar: what was loaded of each product, sold and is left, and what the storeroom still has.")
            .Produces<NightStockResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST and not PUT: it moves the stock, it does not replace it, so
        // sending it twice is not the same as sending it once.
        group
            .MapPost("/{id:guid}/stock/{kdsId:guid}/{productId:guid}/adjust", AdjustStockAsync)
            .WithName("AdjustNightStock")
            .WithSummary("Moves units of a product from the storeroom into one bar of a night that has not ended, or back.")
            .Produces<NightStockResponse>()
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

    internal static async Task<IResult> GetStockAsync(Guid id, INightStockQueries sheets, CancellationToken cancellationToken)
    {
        NightStockSheet? sheet = await sheets.GetAsync(id, cancellationToken);

        return sheet is null ? Rejected(NightErrors.NotFound) : TypedResults.Ok(NightStockResponse.Of(sheet));
    }

    internal static async Task<IResult> AdjustStockAsync(
        Guid id,
        Guid kdsId,
        Guid productId,
        AdjustNightStockRequest request,
        AdjustNightStockHandler handler,
        CancellationToken cancellationToken)
    {
        Result<NightStockSheet> result = await handler.HandleAsync(new AdjustNightStockCommand(id, kdsId, productId, request.Change), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(NightStockResponse.Of(result.Value)) : Rejected(result.Error!);
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
    /// Overlapping argues with the venue's other nights, so it is a conflict, and
    /// so does stock that moved or a night that closed while the screen was
    /// open; anything the table does not name is malformed input.
    /// </summary>
    private static readonly Dictionary<string, int> StatusByErrorCode = new(StringComparer.Ordinal)
    {
        [NightErrors.NotFound.Code] = StatusCodes.Status404NotFound,
        [NightErrors.Overlaps.Code] = StatusCodes.Status409Conflict,
        [NightErrors.StockClosed.Code] = StatusCodes.Status409Conflict,
        [NightErrors.StoreroomShort.Code] = StatusCodes.Status409Conflict,
        [NightErrors.BarHoldsStock.Code] = StatusCodes.Status409Conflict,
        [NightErrors.BarNotFound.Code] = StatusCodes.Status404NotFound,
        [NightErrors.BarNotInNight.Code] = StatusCodes.Status409Conflict,
        [ProductErrors.NotFound.Code] = StatusCodes.Status404NotFound,
        [ProductErrors.StockMoved.Code] = StatusCodes.Status409Conflict,
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
