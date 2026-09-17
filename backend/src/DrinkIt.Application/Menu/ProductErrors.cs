using DrinkIt.Application.Common;

namespace DrinkIt.Application.Menu;

/// <summary>Failures shared by every handler that looks up a product by id.</summary>
public static class ProductErrors
{
    public static readonly Error NotFound =
        new("product.not_found", "This venue has no product with that id.");
}
