namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// What one bar holds of one product (US-37). A bar is a KDS. Not a domain
/// aggregate: it is a counter, and it only ever moves through conditional
/// statements — a sale "take two, but only if there are two", an adjustment
/// "add twelve" — so no total read seconds ago is ever written back over a
/// sale. Product.Stock is the sum of these across every bar.
/// </summary>
internal sealed class BarStock
{
    public Guid VenueId { get; set; }

    public Guid KdsId { get; set; }

    public Guid ProductId { get; set; }

    public int Units { get; set; }
}
