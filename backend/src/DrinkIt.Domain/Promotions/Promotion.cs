using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Promotions;

/// <summary>One product a promotion discounts. Its own row, keyed by the promotion so one cannot repeat.</summary>
public sealed class PromotionProduct
{
    internal PromotionProduct(Guid productId) => ProductId = productId;

    public Guid ProductId { get; }
}

/// <summary>
/// A percentage off some products, on some days of the week, inside a window of
/// the day (US-54): the happy hour.
/// </summary>
/// <remarks>
/// Hours are the venue's own local hours. This class never sees a time zone:
/// whoever asks <see cref="IsOnAt"/> converts first. A window that crosses
/// midnight belongs to the day it starts: Friday 23:00-01:00 is on at Saturday
/// 00:30 and not at Friday 00:30. A night out crosses midnight, so that is the
/// normal case and not an edge.
/// </remarks>
public sealed class Promotion : AuditStamps, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "promotion.venue_required";
        public const string NameRequired = "promotion.name_required";
        public const string NameLength = "promotion.name_length";
        public const string DiscountOutOfRange = "promotion.discount_out_of_range";
        public const string DaysRequired = "promotion.days_required";
        public const string DayInvalid = "promotion.day_invalid";
        public const string WindowEmpty = "promotion.window_empty";
        public const string ProductsRequired = "promotion.products_required";
        public const string DuplicateProduct = "promotion.duplicate_product";
    }

    public const int NameMaxLength = 60;

    private readonly List<PromotionProduct> _products = [];

    // One bit per DayOfWeek (Sunday is bit 0): a set of at most seven days in
    // one column, with no table to join for it.
    private int _days;

    private Promotion(Guid id, Guid venueId)
    {
        Id = id;
        VenueId = venueId;
        Name = string.Empty;
        IsActive = true;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public string Name { get; private set; }

    /// <summary>1 to 99: zero is no promotion and a hundred is a gift.</summary>
    public int DiscountPercent { get; private set; }

    public TimeOnly StartsAt { get; private set; }

    public TimeOnly EndsAt { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The days the window starts on, in week order from Sunday.</summary>
    public IReadOnlyList<DayOfWeek> Days =>
        [.. Enum.GetValues<DayOfWeek>().Where(Runs)];

    /// <summary>The rows behind <see cref="ProductIds"/>; what persistence maps.</summary>
    public IReadOnlyList<PromotionProduct> Products => _products;

    public IReadOnlyList<Guid> ProductIds => [.. _products.Select(product => product.ProductId)];

    public static Promotion Create(
        Guid venueId,
        string name,
        int discountPercent,
        IReadOnlyCollection<DayOfWeek> days,
        TimeOnly startsAt,
        TimeOnly endsAt,
        IReadOnlyCollection<Guid> productIds)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Promotions must belong to a venue.");

        Promotion promotion = new(Guid.CreateVersion7(), venueId);

        promotion.Update(name, discountPercent, days, startsAt, endsAt, productIds);

        return promotion;
    }

    /// <summary>Replaces what the promotion is. Validates all of it first, so a refusal changes nothing.</summary>
    public void Update(
        string name,
        int discountPercent,
        IReadOnlyCollection<DayOfWeek> days,
        TimeOnly startsAt,
        TimeOnly endsAt,
        IReadOnlyCollection<Guid> productIds)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.NameRequired, "The promotion needs a name.");

        string cleanName = name.Trim();

        if (cleanName.Length > NameMaxLength) throw new DomainException(ErrorCodes.NameLength, $"The name cannot be longer than {NameMaxLength} characters.");
        if (discountPercent is < 1 or > 99) throw new DomainException(ErrorCodes.DiscountOutOfRange, "The discount has to be between 1% and 99%.");
        if (days.Count == 0) throw new DomainException(ErrorCodes.DaysRequired, "The promotion needs at least one day.");
        if (days.Any(day => !Enum.IsDefined(day))) throw new DomainException(ErrorCodes.DayInvalid, "The days go from Sunday to Saturday.");
        if (startsAt == endsAt) throw new DomainException(ErrorCodes.WindowEmpty, "The promotion cannot start and end at the same time.");
        if (productIds.Count == 0) throw new DomainException(ErrorCodes.ProductsRequired, "The promotion needs at least one product.");
        if (productIds.Distinct().Count() != productIds.Count) throw new DomainException(ErrorCodes.DuplicateProduct, "A product can be in a promotion only once.");

        Name = cleanName;
        DiscountPercent = discountPercent;
        _days = days.Aggregate(0, (mask, day) => mask | 1 << (int)day);
        StartsAt = startsAt;
        EndsAt = endsAt;

        // Keep the rows that stay and touch only the difference: removing and
        // adding the same key in one save collides, as with a combo's parts.
        _products.RemoveAll(current => !productIds.Contains(current.ProductId));
        _products.AddRange(productIds.Where(id => _products.TrueForAll(current => current.ProductId != id)).Select(id => new PromotionProduct(id)));
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool Covers(Guid productId) => _products.Exists(product => product.ProductId == productId);

    /// <summary>Whether it applies at this local date and time. A deactivated promotion never does.</summary>
    public bool IsOnAt(DateTime local)
    {
        if (!IsActive) return false;

        TimeOnly time = TimeOnly.FromDateTime(local);

        if (StartsAt < EndsAt) return Runs(local.DayOfWeek) && time >= StartsAt && time < EndsAt;

        // Crosses midnight: the evening half belongs to today's day, the
        // morning half to the day before.
        return (Runs(local.DayOfWeek) && time >= StartsAt)
            || (Runs(local.AddDays(-1).DayOfWeek) && time < EndsAt);
    }

    /// <summary>What a product of this regular price costs under the promotion, to the cent.</summary>
    public decimal PriceOf(decimal regular) =>
        Math.Round(regular * (100 - DiscountPercent) / 100m, 2, MidpointRounding.AwayFromZero);

    private bool Runs(DayOfWeek day) => (_days & 1 << (int)day) != 0;
}
