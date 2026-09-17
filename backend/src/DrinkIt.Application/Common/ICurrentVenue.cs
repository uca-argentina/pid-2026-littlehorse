namespace DrinkIt.Application.Common;

/// <summary>
/// The venue the current request belongs to. Resolved from the route slug for
/// customers and from the token claim for staff — never from a parameter the
/// caller controls.
/// </summary>
public interface ICurrentVenue
{
    /// <summary>
    /// Guid.Empty when no venue could be resolved. The query filter then matches
    /// nothing, so an unresolved tenant fails closed instead of exposing rows.
    /// </summary>
    Guid Id { get; }
}
