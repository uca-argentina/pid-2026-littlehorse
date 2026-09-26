using DrinkIt.Api.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Menu;

/// <summary>What the administration screen posts to add a category.</summary>
public sealed record CreateCategoryRequest(string Name);

public sealed record CategoryResponse(Guid Id, string Name);

internal static class CategoriesEndpoints
{
    public static IEndpointRouteBuilder MapCategories(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints
            // No venue slug in the path: staff take their venue from the token
            // claim, so putting it in the URL would be offering a knob that
            // must never be turned. CLAUDE.md, multi-tenancy.
            .MapGroup("/staff/categories")
            .RequireAuthorization(Policies.Administrator)
            .WithTags("Categories");

        group
            .MapGet("/", ListAsync)
            .WithName("ListCategories")
            .WithSummary("Lists the venue's categories, the first one created first.")
            .Produces<IReadOnlyList<CategoryResponse>>();

        group
            .MapPost("/", CreateAsync)
            .WithName("CreateCategory")
            .WithSummary("Adds a category the venue's menu can be split by.")
            .Produces<CategoryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    internal static async Task<IResult> ListAsync(ICategoryQueries categories, CancellationToken cancellationToken)
    {
        IReadOnlyList<CategoryListItem> everything = await categories.ListAsync(cancellationToken);

        return TypedResults.Ok(everything
            .Select(category => new CategoryResponse(category.Id, category.Name))
            .ToArray());
    }

    internal static async Task<IResult> CreateAsync(
        CreateCategoryRequest request,
        CreateCategoryHandler handler,
        CancellationToken cancellationToken)
    {
        // A broken invariant (blank name, too long) is not caught here: the
        // domain throws and the global handler turns it into a 400 with the
        // rule's own problem type.
        Result<CategorySummary> result = await handler.HandleAsync(new CreateCategoryCommand(request.Name), cancellationToken);

        if (!result.IsSuccess) return Rejected(result.Error!);

        // No Location header: there is no endpoint that serves one category on
        // its own, and pointing at the collection would be a lie about what the
        // URI identifies.
        return TypedResults.Created((string?)null, new CategoryResponse(result.Value.Id, result.Value.Name));
    }

    /// <summary>
    /// A taken name is a conflict with the menu, and the screen tells it apart
    /// from a malformed one without reading the message.
    /// </summary>
    private static ProblemHttpResult Rejected(Error error)
    {
        (string title, int status) = error == CreateCategoryHandler.NameTaken
            ? ("Category name already taken", StatusCodes.Status409Conflict)
            : ("Invalid request", StatusCodes.Status400BadRequest);

        return TypedResults.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            type: ProblemTypes.For(error.Code));
    }
}
