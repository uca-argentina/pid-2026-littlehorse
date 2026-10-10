using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Nights;

/// <summary>
/// US-37: how many units of one product a night has. The stock belongs to the
/// night and not to the product, so what one night sold is still readable after
/// the next one starts.
/// </summary>
/// <remarks>
/// <see cref="Loaded"/> is what the night started with plus what the
/// administrator added by hand; <see cref="Remaining"/> is what is left to
/// sell. What was sold is the difference, derived and never stored, so the
/// three cannot disagree. A sale and a cancellation move only
/// <see cref="Remaining"/>, and the repository does it with one relative
/// statement rather than by writing back a number read seconds ago — the same
/// reason <c>Product.Stock</c> was never a concurrency token.
/// </remarks>
public sealed class NightStock : AuditStamps, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "night_stock.venue_required";
        public const string NightRequired = "night_stock.night_required";
        public const string ProductRequired = "night_stock.product_required";
        public const string StockNegative = "night_stock.stock_negative";
        public const string StockChangeZero = "night_stock.stock_change_zero";
    }

    private NightStock(Guid id, Guid venueId, Guid nightId, Guid productId, int loaded, int remaining)
    {
        Id = id;
        VenueId = venueId;
        NightId = nightId;
        ProductId = productId;
        Loaded = loaded;
        Remaining = remaining;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public Guid NightId { get; }

    public Guid ProductId { get; }

    public int Loaded { get; private set; }

    public int Remaining { get; private set; }

    /// <summary>What the night loaded and no longer has. Derived: it cannot disagree with the other two.</summary>
    public int Sold => Loaded - Remaining;

    public bool IsSoldOut => Remaining == 0;

    /// <summary>
    /// The night's stock of one product, starting from what the previous night
    /// had left. The first night of a venue carries over whatever the product
    /// had before nights owned the stock.
    /// </summary>
    public static NightStock Open(Guid venueId, Guid nightId, Guid productId, int carriedOver)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Night stock must belong to a venue.");
        if (nightId == Guid.Empty) throw new DomainException(ErrorCodes.NightRequired, "Night stock must belong to a night.");
        if (productId == Guid.Empty) throw new DomainException(ErrorCodes.ProductRequired, "Night stock must be of a product.");
        if (carriedOver < 0) throw new DomainException(ErrorCodes.StockNegative, "The stock cannot be negative.");

        return new NightStock(Guid.CreateVersion7(), venueId, nightId, productId, carriedOver, carriedOver);
    }

    /// <summary>
    /// Moves the stock by hand: up when a delivery arrives and down when it was
    /// loaded wrong. A change and never a new total, so a sale made while the
    /// screen was open is kept.
    /// </summary>
    public void Adjust(int change)
    {
        if (change == 0) throw new DomainException(ErrorCodes.StockChangeZero, "The stock has to change by at least one unit.");
        if (!CanAdjust(change)) throw new DomainException(ErrorCodes.StockNegative, "The stock cannot be negative.");

        Loaded += change;
        Remaining += change;
    }

    /// <summary>
    /// Whether what is left covers the change. Asked before adjusting: sales
    /// made while the screen was open can leave less than a correction takes
    /// away, and that is something that happens, not a broken rule.
    /// </summary>
    public bool CanAdjust(int change) => Remaining + change >= 0;
}
