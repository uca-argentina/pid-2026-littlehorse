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
    public NewOrderItem(Guid productId, string productName, decimal unitPrice, int quantity, string? note)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        Note = note;
    }

    public Guid ProductId { get; }

    public string ProductName { get; }

    public decimal UnitPrice { get; }

    public int Quantity { get; }

    public string? Note { get; }
}
