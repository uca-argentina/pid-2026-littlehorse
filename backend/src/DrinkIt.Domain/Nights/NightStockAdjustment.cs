using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Nights;

/// <summary>
/// Units of a product loaded into a night, or taken out of it when they were
/// loaded wrong (US-37). Only ever added, never edited: the night's stock is
/// read from these and from its orders, so what was loaded and by whom stays
/// on record.
/// </summary>
/// <remarks>
/// Made by <see cref="Night.AdjustStock"/>, which is where the rules live.
/// </remarks>
public sealed class NightStockAdjustment : AuditStamps, IBelongsToVenue
{
    internal NightStockAdjustment(Guid venueId, Guid nightId, Guid productId, int change)
    {
        Id = Guid.CreateVersion7();
        VenueId = venueId;
        NightId = nightId;
        ProductId = productId;
        Change = change;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public Guid NightId { get; }

    public Guid ProductId { get; }

    /// <summary>Positive when units arrived, negative when they were taken back out.</summary>
    public int Change { get; }
}
