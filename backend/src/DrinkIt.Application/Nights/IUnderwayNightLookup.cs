namespace DrinkIt.Application.Nights;

/// <summary>
/// Which night of the current venue is on at a moment (US-35). Its own port
/// and not a method of <see cref="INightQueries"/>: confirming an order needs
/// this one answer, and its test doubles should not have to fake a listing.
/// </summary>
public interface IUnderwayNightLookup
{
    /// <summary>
    /// The night underway at that moment, or null when the venue is not taking
    /// orders. The end is exclusive, as in <c>Night.IsUnderwayAt</c>.
    /// </summary>
    Task<Guid?> FindIdAsync(DateTimeOffset at, CancellationToken cancellationToken);
}
