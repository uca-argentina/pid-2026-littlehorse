namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// What one bar holds of one product for one night (US-37). A bar is a KDS.
/// Not a domain aggregate: it is a counter, and it only ever moves through
/// conditional statements — a sale "take two, but only if there are two", an
/// adjustment "add twelve" — so no total read seconds ago is ever written back
/// over a sale.
/// </summary>
/// <remarks>
/// Its units are part of Product.Stock, not on top of it: loading a bar moves
/// them out of the storeroom, and a sale takes them off both. Each night starts
/// with no rows, and its rows stop counting against the storeroom once it ends.
/// </remarks>
internal sealed class BarStock
{
    public Guid VenueId { get; set; }

    public Guid NightId { get; set; }

    public Guid KdsId { get; set; }

    public Guid ProductId { get; set; }

    public int Units { get; set; }
}
