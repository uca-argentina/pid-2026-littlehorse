using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Menu;

/// <summary>
/// What the administration screen posts. The image address is whatever the
/// upload returned, or null while there is none.
/// </summary>
public sealed record CreateProductRequest(
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    int Stock);

/// <summary>Where the picture ended up. The listing shows it from here on.</summary>
public sealed record ProductImageResponse(string ImageUrl);

/// <summary>
/// US-08: what the correction form posts. No stock, no picture, no switches —
/// each of those has its own action, so a screen that only touches one of them
/// cannot accidentally overwrite the rest.
/// </summary>
public sealed record UpdateProductRequest(string Name, string? Description, decimal Price);

/// <summary>
/// How much the stock moves: positive when units arrived, negative when it was
/// loaded wrong. Never the new total: a change is what keeps a sale made while
/// the screen was open from being overwritten.
/// </summary>
public sealed record AdjustProductStockRequest(int Change);

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    int Stock,
    bool IsAvailable,
    bool IsSoldOut,
    bool IsActive);

internal static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProducts(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints
            // No venue slug in the path: staff take their venue from the token
            // claim, so putting it in the URL would be offering a knob that
            // must never be turned. CLAUDE.md, multi-tenancy.
            .MapGroup("/staff/products")
            .RequireAuthorization(Policies.Administrator)
            .WithTags("Products");

        group
            .MapGet("/", ListAsync)
            .WithName("ListProducts")
            .WithSummary("Lists every product of the venue, deactivated ones included.")
            .Produces<IReadOnlyList<ProductResponse>>();

        group
            .MapPost("/", CreateAsync)
            .WithName("CreateProduct")
            .WithSummary("Adds a product to the venue's menu.")
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // POST and not DELETE or PUT: nothing is deleted, and the state is a
        // switch, not a field the screen posts a new value for. Same shape as
        // StaffUsersEndpoints' deactivate/reactivate. CLAUDE.md, State pattern.
        group
            .MapPost("/{id:guid}/mark-unavailable", MarkUnavailableAsync)
            .WithName("MarkProductUnavailable")
            .WithSummary("Turns a product's nightly switch off. The customer keeps seeing it, dimmed.")
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPost("/{id:guid}/mark-available", MarkAvailableAsync)
            .WithName("MarkProductAvailable")
            .WithSummary("Turns the switch back on. Does not touch stock.")
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateProduct")
            .WithSummary("Corrects a product's name, description and price.")
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // POST and not PUT: it moves the stock, it does not replace it, so
        // sending it twice is not the same as sending it once.
        group
            .MapPost("/{id:guid}/adjust-stock", AdjustStockAsync)
            .WithName("AdjustProductStock")
            .WithSummary("Moves a product's stock up or down by a number of units.")
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // POST and not DELETE: nothing is deleted. The row stays so the orders
        // that pointed at it keep showing it as it was, exactly the same shape
        // as StaffUsersEndpoints' deactivate. CLAUDE.md, State pattern.
        group
            .MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateProduct")
            .WithSummary("Takes a product off the menu for good. Old orders keep pointing at it.")
            .Produces<ProductResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapPut("/{id:guid}/image", UploadImageAsync)
            .WithName("UploadProductImage")
            .WithSummary("Puts a picture on a product, replacing the one it had. JPEG, PNG or WebP, up to 5 MB.")
            // The form is posted with a bearer token, never from a cookie
            // session, so there is no cross-site request for a token to stop.
            .DisableAntiforgery()
            .Produces<ProductImageResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return endpoints;
    }

    internal static async Task<IResult> ListAsync(IProductQueries products, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductListItem> everything = await products.ListAsync(cancellationToken);

        return TypedResults.Ok(everything
            .Select(product => new ProductResponse(
                product.Id,
                product.Name,
                product.Description,
                product.ImageUrl,
                product.Price,
                product.Stock,
                product.IsAvailable,
                product.IsSoldOut,
                product.IsActive))
            .ToArray());
    }

    internal static async Task<IResult> CreateAsync(
        CreateProductRequest request,
        CreateProductHandler handler,
        CancellationToken cancellationToken)
    {
        // A broken invariant (price at zero, blank name) is not caught here:
        // the domain throws and the global handler turns it into a 400 with the
        // rule's own problem type.
        Result<ProductSummary> result = await handler.HandleAsync(
            new CreateProductCommand(request.Name, request.Description, request.ImageUrl, request.Price, request.Stock),
            cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        // No Location header: there is no endpoint that serves one product on
        // its own yet, and pointing at the collection would be a lie about
        // what the URI identifies.
        return TypedResults.Created((string?)null, Shown(result.Value));
    }

    internal static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        UpdateProductHandler handler,
        CancellationToken cancellationToken)
    {
        // A broken invariant (price at zero, blank name) is not caught here:
        // the domain throws and the global handler answers, same as CreateAsync.
        Result<ProductSummary> result = await handler.HandleAsync(
            new UpdateProductCommand(id, request.Name, request.Description, request.Price),
            cancellationToken);

        return Answer(result);
    }

    internal static async Task<IResult> AdjustStockAsync(
        Guid id,
        AdjustProductStockRequest request,
        AdjustProductStockHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(new AdjustProductStockCommand(id, request.Change), cancellationToken));

    internal static async Task<IResult> DeactivateAsync(
        Guid id,
        DeactivateProductHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, cancellationToken));

    internal static async Task<IResult> MarkUnavailableAsync(
        Guid id,
        MarkProductUnavailableHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, cancellationToken));

    internal static async Task<IResult> MarkAvailableAsync(
        Guid id,
        MarkProductAvailableHandler handler,
        CancellationToken cancellationToken) =>
        Answer(await handler.HandleAsync(id, cancellationToken));

    /// <summary>The updated product, or the failure named so the screen can branch on it.</summary>
    private static IResult Answer(Result<ProductSummary> result) =>
        result.IsSuccess ? TypedResults.Ok(Shown(result.Value)) : Rejected(result.Error!);

    /// <summary>
    /// The one place a written product becomes the JSON the screen reads. The
    /// Angular client is generated from this shape, so it is written once.
    /// </summary>
    private static ProductResponse Shown(ProductSummary product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.ImageUrl,
        product.Price,
        product.Stock,
        product.IsAvailable,
        product.IsSoldOut,
        product.IsActive);

    internal static async Task<IResult> UploadImageAsync(
        Guid id,
        IFormFile? image,
        UploadProductImageHandler handler,
        CancellationToken cancellationToken)
    {
        if (image is null) return ImageIsMissing();

        // The form file is buffered by the framework, so the stream can be read
        // twice: once to tell the format, once to store it.
        await using Stream content = image.OpenReadStream();

        Result<UploadedProductImage> result = await handler.HandleAsync(
            new UploadProductImageCommand(id, content, image.Length),
            cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        return TypedResults.Ok(new ProductImageResponse(result.Value.ImageUrl));
    }

    private static ProblemHttpResult ImageIsMissing() =>
        TypedResults.Problem(
            title: "Invalid request",
            detail: "Send the picture as the 'image' part of a multipart form.",
            statusCode: StatusCodes.Status400BadRequest,
            type: ProblemTypes.For("product.image_required"));

    /// <summary>
    /// Each expected failure with the status that names it, so the screen can
    /// tell them apart without reading the message: a taken name is a conflict
    /// with the menu (409), a missing product is not found (404), a picture
    /// too big is too big (413) and a PDF is not a media type we show (415).
    /// Anything else is malformed input.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error)
    {
        (string title, int status) = error switch
        {
            _ when error == CreateProductHandler.NameTaken => ("Product name already taken", StatusCodes.Status409Conflict),
            _ when error == ProductErrors.NotFound => ("Product not found", StatusCodes.Status404NotFound),
            _ when error == ProductErrors.StockMoved => ("Stock changed meanwhile", StatusCodes.Status409Conflict),
            _ when error == UploadProductImageHandler.ImageTooLarge => ("Picture too large", StatusCodes.Status413PayloadTooLarge),
            _ when error == UploadProductImageHandler.ImageFormatUnsupported => ("Picture format not supported", StatusCodes.Status415UnsupportedMediaType),
            _ => ("Invalid request", StatusCodes.Status400BadRequest),
        };

        return TypedResults.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            type: ProblemTypes.For(error.Code));
    }
}
