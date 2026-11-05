namespace DrinkIt.Domain.Orders;

/// <summary>
/// One line somebody is asking for, on its way into an <see cref="Order"/>.
/// </summary>
/// <remarks>
/// The name and the price are the venue's own, read from its menu at this
/// moment — never what the phone said they were. The phone sends which product
/// and how many; what anything costs is answered here.
///
/// Written out longhand rather than as a positional record: those generate init
/// setters, and DrinkIt.ArchitectureTests refuses a public setter anywhere in
/// the domain. A value that can be rewritten after it was checked is exactly
/// what that rule exists to prevent.
/// </remarks>
public sealed record NewOrderItem
{
    public NewOrderItem(
        Guid productId,
        string productName,
        decimal unitPrice,
        int quantity,
        string? note,
        IReadOnlyList<NewOrderItemComponent>? components = null,
        decimal? regularPrice = null,
        string? promotionName = null)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        Note = note;
        Components = components ?? [];
        RegularPrice = regularPrice;
        PromotionName = promotionName;
    }

    public Guid ProductId { get; }

    public string ProductName { get; }

    public decimal UnitPrice { get; }

    public int Quantity { get; }

    public string? Note { get; }

    /// <summary>What one unit is made of, when it is a combo (US-51). Empty otherwise.</summary>
    public IReadOnlyList<NewOrderItemComponent> Components { get; }

    /// <summary>The list price, when a promotion lowered <see cref="UnitPrice"/> (US-55). Null otherwise.</summary>
    public decimal? RegularPrice { get; }

    /// <summary>The promotion that lowered it, by name. Null otherwise.</summary>
    public string? PromotionName { get; }
}

/// <summary>One product of a combo as the menu has it right now. Longhand for the same reason.</summary>
public sealed record NewOrderItemComponent
{
    public NewOrderItemComponent(Guid productId, string productName, int quantity)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
    }

    public Guid ProductId { get; }

    public string ProductName { get; }

    public int Quantity { get; }
}
