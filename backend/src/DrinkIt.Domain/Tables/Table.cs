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
    }

    /// <summary>The highest number a venue can give a table: three digits fit the label a waiter reads.</summary>
    public const int NumberMaxValue = 999;

    private Table(Guid id, Guid venueId, int number)
    {
        Id = id;
        VenueId = venueId;
        Number = number;
        IsActive = true;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public int Number { get; }

    public bool IsActive { get; private set; }

    public static Table Create(Guid venueId, int number, DateTimeOffset createdAt)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Tables must belong to a venue.");
        if (number is < 1 or > NumberMaxValue) throw new DomainException(ErrorCodes.NumberOutOfRange, $"The table number has to be between 1 and {NumberMaxValue}.");

        return new Table(Guid.CreateVersion7(), venueId, number)
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
}
