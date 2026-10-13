namespace DrinkIt.Domain.Orders;

/// <summary>
/// A move that changes a card on a bar's board (US-15). Names the bar the
/// order is in, so only that bar's tablet redraws (US-36): with several bars,
/// the others have nothing new to show.
/// </summary>
public interface IBoardChanged : IOrderChanged
{
    Guid VenueId { get; }

    /// <summary>Null only on orders from before bars kept a stock of their own: no board shows them.</summary>
    Guid? KdsId { get; }
}
