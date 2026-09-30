namespace DrinkIt.Application.Venues;

/// <summary>
/// Every venue, for the work that runs on its own and not inside a request —
/// cancelling unpaid orders (US-24) — and so has to go venue by venue.
/// </summary>
public interface IVenueDirectory
{
    Task<IReadOnlyList<Guid>> AllIdsAsync(CancellationToken cancellationToken);
}
