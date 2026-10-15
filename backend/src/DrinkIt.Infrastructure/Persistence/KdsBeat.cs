namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// The last time a bar's tablet said it was there (US-38). A bar is a KDS. Not
/// a domain aggregate: one row per tablet, moved forward every twenty seconds
/// by its board, and read when an order is paid to leave out the bars that
/// went quiet.
/// </summary>
/// <remarks>
/// Kept in the database and not in the hub's memory: the API runs up to two
/// replicas and scales to zero, so a tablet connected to one of them is
/// invisible to the other (ADR-0007).
/// </remarks>
internal sealed class KdsBeat
{
    public Guid VenueId { get; set; }

    public Guid KdsId { get; set; }

    public DateTimeOffset LastBeatAt { get; set; }
}
