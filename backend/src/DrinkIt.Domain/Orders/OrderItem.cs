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

    private readonly List<OrderItemComponent> _components = [];

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

    /// <summary>
    /// What one unit is made of when it is a combo (US-51), as it was when the
    /// order was confirmed: cash takes its stock later, and the combo may have
    /// changed by then. Empty on anything else.
    /// </summary>
    public IReadOnlyList<OrderItemComponent> Components => _components;

    public decimal Total => UnitPrice * Quantity;

    internal void Freeze(IEnumerable<NewOrderItemComponent> components) =>
        _components.AddRange(components.Select(component => new OrderItemComponent(component.ProductId, component.ProductName, component.Quantity)));
}

/// <summary>One product of a combo, frozen in the order that sold it (US-51).</summary>
public sealed class OrderItemComponent
{
    internal OrderItemComponent(Guid productId, string productName, int quantity)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
    }

    public Guid ProductId { get; }

    /// <summary>What it was called the night it was ordered.</summary>
    public string ProductName { get; }

    /// <summary>How many of it go in one unit of the combo.</summary>
    public int Quantity { get; }
}
