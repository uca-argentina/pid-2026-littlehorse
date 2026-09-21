using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Menu;

/// <summary>
/// Something the venue sells from its menu: a mixed drink, a bottle, a beer.
/// The customer reads it, picks it and pays for it from their phone, so what
/// lives here is what that screen needs and the rules that keep it honest.
/// </summary>
/// <remarks>
/// Three separate flags, decided on 2026-09-14, because they answer three
/// different questions:
/// <list type="bullet">
/// <item><see cref="Stock"/> — how many are left. Reaching zero sells the product out on its own.</item>
/// <item><see cref="IsAvailable"/> — the nightly switch. Off while there is still stock, e.g. the ice machine broke.</item>
/// <item><see cref="IsActive"/> — the soft delete. Off means gone from the menu for good; the row stays because old orders point at it.</item>
/// </list>
/// </remarks>
public sealed class Product : IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "product.venue_required";
        public const string NameRequired = "product.name_required";
        public const string NameLength = "product.name_length";
        public const string DescriptionLength = "product.description_length";
        public const string PriceNotPositive = "product.price_not_positive";
        public const string StockNegative = "product.stock_negative";
        public const string ImageUrlInvalid = "product.image_url_invalid";
        public const string SoldOutCannotBeAvailable = "product.sold_out_cannot_be_available";
    }

    /// <summary>Read on a phone, at night, in a hurry: a name has to fit on one line of a card.</summary>
    public const int NameMaxLength = 80;

    public const int DescriptionMaxLength = 200;

    private Product(
        Guid id,
        Guid venueId,
        string name,
        string? description,
        string? imageUrl,
        decimal price,
        int stock)
    {
        Id = id;
        VenueId = venueId;
        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        Price = price;
        Stock = stock;
        IsAvailable = true;
        IsActive = true;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// Absolute http(s) address of the picture, or null when there is none and
    /// the menu shows a placeholder. The domain does not care where it is
    /// hosted; the upload is the infrastructure's business.
    /// </summary>
    public string? ImageUrl { get; private set; }

    public decimal Price { get; private set; }

    public int Stock { get; private set; }

    /// <summary>True when the venue can serve it tonight. Independent of stock.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>False after the ABM's soft delete. Rows are never removed.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Nothing left to sell. Derived, never stored: it cannot disagree with <see cref="Stock"/>.</summary>
    public bool IsSoldOut => Stock == 0;

    /// <summary>
    /// What the customer's menu is allowed to offer: the switch says yes and
    /// there is something left. Derived from the two, so no screen and no query
    /// has to remember to ask both.
    /// </summary>
    /// <remarks>
    /// The menu query restates this as SQL, because neither this nor
    /// <see cref="IsSoldOut"/> is a mapped column. This is where the rule is
    /// owned and where its test lives.
    /// </remarks>
    public bool IsOrderable => IsAvailable && !IsSoldOut;

    public static Product Create(
        Guid venueId,
        string name,
        string? description,
        string? imageUrl,
        decimal price,
        int stock)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Products must belong to a venue.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.NameRequired, "The product needs a name.");

        string cleanName = name.Trim();

        if (cleanName.Length > NameMaxLength) throw new DomainException(ErrorCodes.NameLength, $"The name cannot be longer than {NameMaxLength} characters.");

        string? cleanDescription = BlankToNull(description);

        if (cleanDescription?.Length > DescriptionMaxLength) throw new DomainException(ErrorCodes.DescriptionLength, $"The description cannot be longer than {DescriptionMaxLength} characters.");
        if (price <= 0) throw new DomainException(ErrorCodes.PriceNotPositive, "The price has to be greater than zero.");
        if (stock < 0) throw new DomainException(ErrorCodes.StockNegative, "The stock cannot be negative.");

        string? cleanImageUrl = BlankToNull(imageUrl);

        if (cleanImageUrl is not null) EnsureAbsoluteHttpUrl(cleanImageUrl);

        return new Product(Guid.CreateVersion7(), venueId, cleanName, cleanDescription, cleanImageUrl, price, stock);
    }

    /// <summary>
    /// US-07: the nightly switch, off. Always allowed, sold out included — the
    /// administrator taps it without first working out what state it was in.
    /// </summary>
    public void MarkUnavailable() => IsAvailable = false;

    /// <summary>
    /// US-07: the switch back on, e.g. after the ice arrived. Refused when
    /// there is nothing left to pour: running out is not something a switch can
    /// undo, and the way back is restocking the product, which is US-08's job.
    /// Until then the screen keeps the switch off and locked.
    /// </summary>
    public void MarkAvailable()
    {
        if (IsSoldOut) throw new DomainException(ErrorCodes.SoldOutCannotBeAvailable, "A product with no stock left cannot be put back on sale. Add stock first.");

        IsAvailable = true;
    }

    /// <summary>
    /// The picture arrives after the product exists — the upload needs an id to
    /// file it under — and can be swapped later. The address is whatever the
    /// storage handed back; the same rule as at creation applies to it.
    /// </summary>
    public void ReplaceImage(string imageUrl)
    {
        string cleanImageUrl = BlankToNull(imageUrl)
            ?? throw new DomainException(ErrorCodes.ImageUrlInvalid, "The image address has to be a full http or https link.");

        EnsureAbsoluteHttpUrl(cleanImageUrl);

        ImageUrl = cleanImageUrl;
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The address ends up in an img src on the customer's phone: it has to be
    /// something a browser fetches and nothing a browser would execute.
    /// </summary>
    private static void EnsureAbsoluteHttpUrl(string value)
    {
        bool isHttp = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        if (!isHttp) throw new DomainException(ErrorCodes.ImageUrlInvalid, "The image address has to be a full http or https link.");
    }
}
