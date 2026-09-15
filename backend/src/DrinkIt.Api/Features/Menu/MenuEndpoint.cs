using DrinkIt.Api.Common;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Menu;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrinkIt.Api.Features.Menu;

/// <summary>One card of the menu, as the customer's phone receives it.</summary>
public sealed record MenuItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    bool IsOrderable);

/// <summary>
/// The venue's menu. The name travels with it because the customer scanned a
/// QR and never typed where they are: the screen is what tells them.
/// </summary>
public sealed record MenuResponse(string VenueName, IReadOnlyList<MenuItemResponse> Items);

internal static class MenuEndpoint
{
    public static IEndpointRouteBuilder MapMenu(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            // The slug is in the path because this is the customer's door and
            // there is no token to carry a venue. Knowing a slug grants nothing
            // it does not already grant: the menu is on a poster on the wall.
            .MapGet("/{venueSlug}/menu", GetAsync)
            .AllowAnonymous()
            .WithName("GetMenu")
            .WithTags("Menu")
            .WithSummary("The menu a customer reads after scanning the venue's QR.")
            .Produces<MenuResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>
    /// Both halves of the answer come from the one venue the middleware
    /// resolved from the slug: the name from what it remembered, the products
    /// from the query filter it set. Looking the venue up again here is how a
    /// single response ends up naming one venue and listing another's drinks.
    /// </summary>
    internal static async Task<IResult> GetAsync(
        string venueSlug,
        CurrentVenue venue,
        IProductQueries products,
        CancellationToken cancellationToken)
    {
        // Told apart from a venue that exists and has nothing loaded on
        // purpose: one is a wrong address and the other is a bar that has not
        // finished setting up, and they read nothing alike on a phone.
        if (venue.Identity is null) return NoSuchVenue(venueSlug);

        IReadOnlyList<MenuItem> menu = await products.ListForMenuAsync(cancellationToken);

        return TypedResults.Ok(new MenuResponse(
            venue.Identity.Name,
            [.. menu.Select(item => new MenuItemResponse(
                item.Id,
                item.Name,
                item.Description,
                item.ImageUrl,
                item.Price,
                item.IsOrderable))]));
    }

    private static ProblemHttpResult NoSuchVenue(string slug) =>
        TypedResults.Problem(
            title: "Not found",
            detail: $"No venue is reached at '{slug}'.",
            statusCode: StatusCodes.Status404NotFound,
            type: ProblemTypes.For("venue.not_found"));
}
