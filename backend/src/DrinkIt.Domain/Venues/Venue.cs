using System.Text.RegularExpressions;
using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Venues;

/// <summary>
/// A place that sells drinks: a club, a bar, the bars of a concert venue.
/// Root of the multi-tenancy model — see CLAUDE.md.
/// </summary>
public sealed partial class Venue : CreationStamp
{
    public static class ErrorCodes
    {
        public const string NameRequired = "venue.name_required";
        public const string NameTooLong = "venue.name_too_long";
        public const string SlugRequired = "venue.slug_required";
        public const string SlugLength = "venue.slug_length";
        public const string SlugNotUrlSafe = "venue.slug_not_url_safe";
    }

    private const int SlugMinLength = 3;
    private const int SlugMaxLength = 50;
    private const int NameMaxLength = 100;

    private Venue(Guid id, string name, string slug)
    {
        Id = id;
        Name = name;
        Slug = slug;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    /// <summary>What the customer's QR resolves to: /{slug}/menu.</summary>
    public string Slug { get; private set; }

    public static Venue Create(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.NameRequired, "Venue name is required.");

        string cleanName = name.Trim();
        if (cleanName.Length > NameMaxLength) throw new DomainException(ErrorCodes.NameTooLong, $"Venue name cannot exceed {NameMaxLength} characters.");

        if (string.IsNullOrWhiteSpace(slug)) throw new DomainException(ErrorCodes.SlugRequired, "Venue slug is required.");

        string cleanSlug = slug.Trim();
        if (cleanSlug.Length is < SlugMinLength or > SlugMaxLength) throw new DomainException(ErrorCodes.SlugLength, $"Venue slug must be between {SlugMinLength} and {SlugMaxLength} characters.");
        if (!SlugPattern().IsMatch(cleanSlug)) throw new DomainException(ErrorCodes.SlugNotUrlSafe, $"Venue slug '{cleanSlug}' is not URL-safe.");

        // Version 7 GUIDs are time-ordered, so rows land at the end of the
        // clustered index instead of scattering across it like v4 does.
        return new Venue(Guid.CreateVersion7(), cleanName, cleanSlug);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
