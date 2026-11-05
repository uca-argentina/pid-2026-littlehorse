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

    /// <summary>
    /// The venue of the current request has no category with that id. Another
    /// venue's category lands here too, for the same reason as
    /// <see cref="NotFound"/>.
    /// </summary>
    public static readonly Error CategoryNotFound =
        new("product.category_not_found", "This venue has no category with that id.");

    /// <summary>US-50: a component is not on this venue's menu, for the same reason as <see cref="NotFound"/>.</summary>
    public static readonly Error ComboComponentNotFound =
        new("product.combo_component_not_found", "One of the combo's products is not on this venue's menu.");

    public static readonly Error ComboInsideCombo =
        new("product.combo_inside_combo", "A combo cannot have another combo among its products.");

    /// <summary>US-50: only its components have a stock to move.</summary>
    public static readonly Error ComboHasNoStock =
        new("product.combo_has_no_stock", "A combo has no stock of its own: adjust its products.");

    /// <summary>
    /// Sales since the screen was opened left less stock than the change takes
    /// away. Worth its own answer: looking again at the stock is the fix.
    /// </summary>
    public static readonly Error StockMoved =
        new("product.stock_moved", "Sales changed the stock; there is less left than that takes away.");
}
