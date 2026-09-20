using DrinkIt.Application.Common;

namespace DrinkIt.Application.Menu;

/// <summary>Failures shared by every handler that looks up a product by id.</summary>
public static class ProductErrors
{
    /// <summary>
    /// The venue of the current request has no product with that id. Another
    /// venue's product lands here too: from outside, not ours and not existing
    /// are the same answer, and telling them apart would leak what the venue
    /// next door sells.
    /// </summary>
    public static readonly Error NotFound =
        new("product.not_found", "This venue has no product with that id.");
}
