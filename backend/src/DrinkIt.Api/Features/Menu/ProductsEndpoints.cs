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
        Result<CreatedProduct> result = await handler.HandleAsync(
            new CreateProductCommand(request.Name, request.Description, request.ImageUrl, request.Price, request.Stock),
            cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        CreatedProduct created = result.Value;

        // No Location header: there is no endpoint that serves one product on
        // its own yet, and pointing at the collection would be a lie about
        // what the URI identifies.
        return TypedResults.Created(
            (string?)null,
            new ProductResponse(
                created.Id,
                created.Name,
                created.Description,
                created.ImageUrl,
                created.Price,
                created.Stock,
                created.IsAvailable,
                created.IsSoldOut,
                created.IsActive));
    }

    /// <summary>
    /// A taken name is a conflict with the current state of the menu, not
    /// malformed input, so it is a 409 and the screen can tell the two apart
    /// without reading the message.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error) =>
        TypedResults.Problem(
            title: error == CreateProductHandler.NameTaken ? "Product name already taken" : "Invalid request",
            detail: error.Message,
            statusCode: error == CreateProductHandler.NameTaken
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest,
            type: ProblemTypes.For(error.Code));
}
