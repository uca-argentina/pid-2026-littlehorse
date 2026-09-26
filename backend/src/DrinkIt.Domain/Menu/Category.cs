using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Menu;

/// <summary>
/// One of the tabs a venue's menu is split into (US-14): "Tragos", "Cervezas",
/// whatever the administrator decides to sell under. Data and not an enum since
/// 2026-09-26, because each venue names its own; the three the platform started
/// with are just the first rows every venue got.
/// </summary>
public sealed class Category : IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "category.venue_required";
        public const string NameRequired = "category.name_required";
        public const string NameLength = "category.name_length";
    }

    /// <summary>A tab on a phone: what does not fit on one line of it is not a name.</summary>
    public const int NameMaxLength = 40;

    private Category(Guid id, Guid venueId, string name, DateTimeOffset createdAt)
    {
        Id = id;
        VenueId = venueId;
        Name = name;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public string Name { get; }

    /// <summary>What orders the tabs: the first one created is the first one shown.</summary>
    public DateTimeOffset CreatedAt { get; }

    public static Category Create(Guid venueId, string name, DateTimeOffset createdAt)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Categories must belong to a venue.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.NameRequired, "The category needs a name.");

        string cleanName = name.Trim();

        if (cleanName.Length > NameMaxLength) throw new DomainException(ErrorCodes.NameLength, $"The name cannot be longer than {NameMaxLength} characters.");

        return new Category(Guid.CreateVersion7(), venueId, cleanName, createdAt);
    }
}
