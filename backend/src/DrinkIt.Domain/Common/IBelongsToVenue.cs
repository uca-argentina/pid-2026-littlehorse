namespace DrinkIt.Domain.Common;

/// <summary>
/// Marks an aggregate that lives inside one venue. Everything implementing this
/// must be filtered by the current venue, and DrinkIt.ArchitectureTests fails
/// the build if one of them is not.
/// </summary>
public interface IBelongsToVenue
{
    Guid VenueId { get; }
}
