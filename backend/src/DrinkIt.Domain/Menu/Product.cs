using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Menu;

/// <summary>
/// Something the venue sells from its menu: a mixed drink, a bottle, a beer.
/// The customer reads it, picks it and pays for it from their phone, so what
/// lives here is what that screen needs and the rules that keep it honest.
/// </summary>
/// <remarks>
/// Two separate flags, decided on 2026-09-14, because they answer different
/// questions. How many are left is not one of them any more: since US-37 that
/// belongs to the night, and this class only keeps the number the product was
/// made with (<see cref="InitialStock"/>).
/// <list type="bullet">
/// <item><see cref="IsAvailable"/> — the nightly switch. Off while there is still stock, e.g. the ice machine broke.</item>
/// <item><see cref="IsActive"/> — the soft delete. Off means gone from the menu for good; the row stays because old orders point at it.</item>
/// </list>
/// </remarks>
public sealed class Product : AuditStamps, IBelongsToVenue
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
        public const string CategoryRequired = "product.category_required";
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
        int initialStock,
        Guid categoryId)
    {
        Id = id;
        VenueId = venueId;
        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        Price = price;
        InitialStock = initialStock;
        CategoryId = categoryId;
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

    /// <summary>
    /// How many units the product was made with, and so where the first night
    /// that sells it starts from. Never moved by a sale: what a night has left
    /// is <see cref="DrinkIt.Domain.Nights.NightStock"/>.
    /// </summary>
    public int InitialStock { get; private set; }

    /// <summary>
    /// US-14: the tab this product shows under. Only the id: that the venue has
    /// such a category is checked where both can be looked up, not here.
    /// </summary>
    public Guid CategoryId { get; private set; }

    /// <summary>True when the venue can serve it tonight. Independent of stock.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>False after the ABM's soft delete. Rows are never removed.</summary>
    public bool IsActive { get; private set; }

    public static Product Create(
        Guid venueId,
        string name,
        string? description,
        string? imageUrl,
        decimal price,
        int initialStock,
        Guid categoryId)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Products must belong to a venue.");
        string cleanName = ValidateName(name);
        string? cleanDescription = ValidateDescription(description);

        ValidatePrice(price);

        if (initialStock < 0) throw new DomainException(ErrorCodes.StockNegative, "The stock cannot be negative.");
        ValidateCategory(categoryId);

        string? cleanImageUrl = BlankToNull(imageUrl);

        if (cleanImageUrl is not null) EnsureAbsoluteHttpUrl(cleanImageUrl);

        return new Product(Guid.CreateVersion7(), venueId, cleanName, cleanDescription, cleanImageUrl, price, initialStock, categoryId);
    }

    /// <summary>
    /// US-07: the nightly switch, off. Always allowed: the administrator taps
    /// it without first working out what state it was in.
    /// </summary>
    public void MarkUnavailable() => IsAvailable = false;

    /// <summary>
    /// US-07: the switch back on, e.g. after the ice arrived. Whether the night
    /// has any left is not this switch's business: the menu reads that from the
    /// night's stock.
    /// </summary>
    public void MarkAvailable() => IsAvailable = true;

    /// <summary>
    /// US-08: the name, description and price a customer reads, plus US-14's
    /// category. The picture and the two switches each have their own
    /// dedicated method — a single "update everything" invites forgetting one
    /// of them halfway through a screen.
    /// </summary>
    public void Update(string name, string? description, decimal price, Guid categoryId)
    {
        string cleanName = ValidateName(name);
        string? cleanDescription = ValidateDescription(description);

        ValidatePrice(price);
        ValidateCategory(categoryId);

        Name = cleanName;
        Description = cleanDescription;
        Price = price;
        CategoryId = categoryId;
    }

    /// <summary>US-08: off the menu for good. The row stays for the orders that already point at it.</summary>
    public void Deactivate() => IsActive = false;

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.NameRequired, "The product needs a name.");

        string cleanName = name.Trim();

        if (cleanName.Length > NameMaxLength) throw new DomainException(ErrorCodes.NameLength, $"The name cannot be longer than {NameMaxLength} characters.");

        return cleanName;
    }

    private static string? ValidateDescription(string? description)
    {
        string? cleanDescription = BlankToNull(description);

        if (cleanDescription?.Length > DescriptionMaxLength) throw new DomainException(ErrorCodes.DescriptionLength, $"The description cannot be longer than {DescriptionMaxLength} characters.");

        return cleanDescription;
    }

    private static void ValidatePrice(decimal price)
    {
        if (price <= 0) throw new DomainException(ErrorCodes.PriceNotPositive, "The price has to be greater than zero.");
    }

    private static void ValidateCategory(Guid categoryId)
    {
        if (categoryId == Guid.Empty) throw new DomainException(ErrorCodes.CategoryRequired, "The product needs a category.");
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
