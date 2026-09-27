namespace DrinkIt.Domain.Orders;

/// <summary>
/// One drink in an order, with however many of it were asked for and whatever
/// was written on it.
/// </summary>
/// <remarks>
/// The name and the price are copied in beside the product's id, on purpose: a
/// ticket has to say what was charged the night it was charged, and the menu
/// moves. Nothing here reads back through <see cref="ProductId"/> to find out
/// what something cost.
/// </remarks>
public sealed class OrderItem
{
    /// <summary>Long enough for "sin hielo, con mucho limón", short enough to read at a glance at the bar.</summary>
    public const int NoteMaxLength = 120;

    internal OrderItem(Guid id, Guid productId, string productName, decimal unitPrice, int quantity, string? note)
    {
        Id = id;
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        Note = note;
    }

    public Guid Id { get; }

    public Guid ProductId { get; }

    /// <summary>What it was called the night it was ordered.</summary>
    public string ProductName { get; }

    /// <summary>What it cost that night.</summary>
    public decimal UnitPrice { get; }

    public int Quantity { get; }

    /// <summary>"sin hielo". Belongs to this drink and not to the whole order.</summary>
    public string? Note { get; }

    public decimal Total => UnitPrice * Quantity;
}
