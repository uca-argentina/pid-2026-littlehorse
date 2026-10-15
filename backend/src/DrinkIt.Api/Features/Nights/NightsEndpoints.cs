using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Metrics;
using DrinkIt.Application.Nights;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Nights;

/// <summary>What the administration screen posts to set up a night.</summary>
/// <remarks>US-35.</remarks>
/// <summary>One VIP table the night sells, with the budget of its package (US-40).</summary>
public sealed record NightTableRequestBody(Guid TableId, decimal Budget);

public sealed record CreateNightRequest(
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<Guid> CrewIds,
    IReadOnlyList<NightTableRequestBody>? Tables = null);

/// <summary>What the night's own screen saves: the whole form, as on creation.</summary>
/// <remarks>
/// Tables is the whole list the night ends up with: a table left out is taken
/// out of the night. Left out altogether, the night's tables stay as they are.
/// </remarks>
public sealed record UpdateNightRequest(
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<Guid> CrewIds,
    IReadOnlyList<NightTableRequestBody>? Tables = null);

/// <summary>
/// A table of the night with what was loaded onto it, what its orders took,
/// and the code of its QR for the waiter to hand over.
/// </summary>
public sealed record NightTableResponse(Guid TableId, int Number, int Capacity, string? Location, decimal Budget, decimal Spent, string AccessCode);

/// <summary>The new code of a table after the old one leaked.</summary>
public sealed record NightTableCodeResponse(Guid TableId, string AccessCode);

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

/// <summary>How long a stage took, in whole seconds. Null until some order has been through it.</summary>
public sealed record StageTimesResponse(int? AverageSeconds, int? WorstTenthSeconds)
{
    internal static StageTimesResponse Of(StageTimes times) => new(SecondsOf(times.Average), SecondsOf(times.WorstTenth));

    private static int? SecondsOf(TimeSpan? time) => time is TimeSpan measured ? (int)Math.Round(measured.TotalSeconds) : null;
}

/// <summary>What was paid with one method. Every method has a row, even at zero.</summary>
public sealed record SalesByMethodResponse(PaymentMethodName Method, int Orders, decimal Amount);

/// <summary>From paid to ready at one bar of the night, by the name its board shows.</summary>
public sealed record BarTimesResponse(Guid KdsId, string Name, StageTimesResponse PaidToReady);

/// <summary>
/// One of the most ordered products, with the name and what it billed as it
/// was ordered, beside what its bars were loaded with that night.
/// </summary>
public sealed record TopProductResponse(Guid ProductId, string Name, int Units, decimal Billed, int Loaded, bool SoldOut);

/// <summary>
/// A night's report. WorstTenthSeconds is the average of the slowest tenth of
/// the orders, not a percentile. Unmeasured counts the orders from before
/// ReadyAt was kept, which have no time to give.
/// </summary>
/// <remarks>US-47 to US-49.</remarks>
public sealed record NightMetricsResponse(
    Guid NightId,
    decimal Sold,
    int Orders,
    decimal AverageTicket,
    int Units,
    IReadOnlyList<SalesByMethodResponse> ByMethod,
    StageTimesResponse PaidToReady,
    StageTimesResponse ReadyToDelivered,
    IReadOnlyList<BarTimesResponse> ByBar,
    int Unmeasured,
    IReadOnlyList<TopProductResponse> Top,
    int SoldOutProducts)
{
    internal static NightMetricsResponse Of(Guid nightId, NightMetrics metrics) => new(
        nightId,
        metrics.Sold,
        metrics.Orders,
        metrics.AverageTicket,
        metrics.Units,
        [.. metrics.ByMethod.Select(row => new SalesByMethodResponse(row.Method.ToContract(), row.Orders, row.Amount))],
        StageTimesResponse.Of(metrics.PaidToReady),
        StageTimesResponse.Of(metrics.ReadyToDelivered),
        [.. metrics.ByBar.Select(bar => new BarTimesResponse(bar.KdsId, bar.Name, StageTimesResponse.Of(bar.PaidToReady)))],
        metrics.Unmeasured,
        [.. metrics.Top.Select(product => new TopProductResponse(product.ProductId, product.Name, product.Units, product.Billed, product.Loaded, product.SoldOut))],
        metrics.SoldOutProducts);
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

        group
            .MapGet("/{id:guid}/tables", GetTablesAsync)
            .WithName("GetNightTables")
            .WithSummary("The VIP tables the night sells, by number, with the budget of each package and what it spent.")
            .Produces<IReadOnlyList<NightTableResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST: every call makes a new code, so sending it twice is not the
        // same as sending it once.
        group
            .MapPost("/{id:guid}/tables/{tableId:guid}/access-code", RegenerateTableCodeAsync)
            .WithName("RegenerateNightTableCode")
            .WithSummary("Gives a table of the night a new code for its QR. The old one stops working; the balance stays.")
            .Produces<NightTableCodeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapGet("/{id:guid}/metrics", GetMetricsAsync)
            .WithName("GetNightMetrics")
            .WithSummary("The night's report: what was sold and how it was paid, how long orders took, and the most ordered products against their stock. So far, for a night still underway.")
            .Produces<NightMetricsResponse>()
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

    internal static async Task<IResult> GetTablesAsync(Guid id, INightTableQueries tables, CancellationToken cancellationToken)
    {
        IReadOnlyList<NightTableLine>? lines = await tables.ListAsync(id, cancellationToken);

        return lines is null
            ? Rejected(NightErrors.NotFound)
            : TypedResults.Ok(lines.Select(line => new NightTableResponse(line.TableId, line.Number, line.Capacity, line.Location, line.Budget, line.Spent, line.AccessCode)).ToArray());
    }

    internal static async Task<IResult> RegenerateTableCodeAsync(
        Guid id,
        Guid tableId,
        RegenerateTableCodeHandler handler,
        CancellationToken cancellationToken)
    {
        Result<NightTableCode> result = await handler.HandleAsync(id, tableId, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(new NightTableCodeResponse(result.Value.TableId, result.Value.AccessCode))
            : Rejected(result.Error!);
    }

    internal static async Task<IResult> GetMetricsAsync(Guid id, GetNightMetricsHandler handler, CancellationToken cancellationToken)
    {
        Result<NightMetrics> result = await handler.HandleAsync(id, cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(NightMetricsResponse.Of(id, result.Value)) : Rejected(result.Error!);
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
            new UpdateNightCommand(id, request.Name, request.StartsAt, request.EndsAt, request.CrewIds ?? [], request.Tables is null ? null : TablesOf(request.Tables)),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(NightResponse.Of(result.Value)) : Rejected(result.Error!);
    }

    internal static async Task<IResult> CreateAsync(
        CreateNightRequest request,
        CreateNightHandler handler,
        CancellationToken cancellationToken)
    {
        Result<NightSummary> result = await handler.HandleAsync(
            new CreateNightCommand(request.Name, request.StartsAt, request.EndsAt, request.CrewIds ?? [], TablesOf(request.Tables)),
            cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        // No Location header, for the same reason as staff users: nothing
        // serves one night on its own yet.
        return TypedResults.Created((string?)null, NightResponse.Of(result.Value));
    }

    private static NightTableRequest[] TablesOf(IReadOnlyList<NightTableRequestBody>? tables) =>
        [.. (tables ?? []).Select(table => new NightTableRequest(table.TableId, table.Budget))];

    /// <summary>
    /// Overlapping argues with the venue's other nights, so it is a conflict, and
    /// so does stock that moved or a night that closed while the screen was
    /// open; anything the table does not name is malformed input.
    /// </summary>
    private static readonly Dictionary<string, int> StatusByErrorCode = new(StringComparer.Ordinal)
    {
        [NightErrors.NotFound.Code] = StatusCodes.Status404NotFound,
        [NightErrors.TableNotInNight.Code] = StatusCodes.Status404NotFound,
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
