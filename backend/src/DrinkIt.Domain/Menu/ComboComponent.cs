namespace DrinkIt.Domain.Menu;

/// <summary>
/// One product a combo is made of, and how many of it go in one combo (US-50).
/// Only the id: that the venue sells such a product is checked where both can
/// be looked up, not here.
/// </summary>
public sealed class ComboComponent
{
    internal ComboComponent(Guid productId, int quantity)
    {
        ProductId = productId;
        Quantity = quantity;
    }

    public Guid ProductId { get; }

    public int Quantity { get; internal set; }
}
