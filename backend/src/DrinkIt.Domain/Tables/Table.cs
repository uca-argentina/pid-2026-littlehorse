using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Tables;

/// <summary>
/// A table of the VIP sector (US-39), sold as a package. Its number is what the
/// customer and the waiter read; the account that holds its balance (US-40) hangs
/// from it, one night at a time.
/// </summary>
public sealed class Table : AuditStamps, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "table.venue_required";
        public const string NumberOutOfRange = "table.number_out_of_range";
        public const string CapacityOutOfRange = "table.capacity_out_of_range";
        public const string LocationLength = "table.location_length";
    }

    /// <summary>The highest number a venue can give a table: three digits fit the label a waiter reads.</summary>
    public const int NumberMaxValue = 999;

    /// <summary>The most people a package can be sold for: a VIP table, not a whole sector.</summary>
    public const int CapacityMaxValue = 50;

    /// <summary>Short enough for the line under "Mesa 5" in the listing and the night's tables.</summary>
    public const int LocationMaxLength = 60;

    private Table(Guid id, Guid venueId, int number, int capacity, string? location)
    {
        Id = id;
        VenueId = venueId;
        Number = number;
        Capacity = capacity;
        Location = location;
        IsActive = true;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public int Number { get; }

    /// <summary>How many people the package is sold for.</summary>
    public int Capacity { get; private set; }

    /// <summary>Where it sits, in the venue's own words ("Al lado de la cabina"). Optional.</summary>
    public string? Location { get; private set; }

    public bool IsActive { get; private set; }

    public static Table Create(Guid venueId, int number, int capacity, string? location, DateTimeOffset createdAt)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Tables must belong to a venue.");
        if (number is < 1 or > NumberMaxValue) throw new DomainException(ErrorCodes.NumberOutOfRange, $"The table number has to be between 1 and {NumberMaxValue}.");

        string? cleanLocation = CheckDescription(capacity, location);

        return new Table(Guid.CreateVersion7(), venueId, number, capacity, cleanLocation)
        {
            CreatedAt = createdAt,
        };
    }

    /// <summary>
    /// Takes the table out of use until <see cref="Activate"/> brings it back. The
    /// row stays: old orders keep pointing at it, and its number stays taken.
    /// </summary>
    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <summary>How many people it seats and where it is. The number never changes: orders say it.</summary>
    public void Describe(int capacity, string? location)
    {
        string? cleanLocation = CheckDescription(capacity, location);

        Capacity = capacity;
        Location = cleanLocation;
    }

    /// <summary>The rules Create and Describe share, checked before anything changes. Returns the location as kept.</summary>
    private static string? CheckDescription(int capacity, string? location)
    {
        if (capacity is < 1 or > CapacityMaxValue) throw new DomainException(ErrorCodes.CapacityOutOfRange, $"A table seats between 1 and {CapacityMaxValue} people.");

        string? cleanLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();

        if (cleanLocation?.Length > LocationMaxLength) throw new DomainException(ErrorCodes.LocationLength, $"The location cannot be longer than {LocationMaxLength} characters.");

        return cleanLocation;
    }
}
