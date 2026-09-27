namespace DrinkIt.Infrastructure.Persistence;

/// <summary>
/// Where a venue's order codes are up to. One row per venue.
/// </summary>
/// <remarks>
/// Not a domain entity: there is no rule about it worth writing down, only the
/// question of who hands out the next number without two people getting the
/// same one. That question is the database's, so this lives here.
/// </remarks>
internal sealed class OrderCodeCounter
{
    public Guid VenueId { get; set; }

    /// <summary>The last code this venue handed out, as its six characters.</summary>
    public string LastCode { get; set; } = string.Empty;
}
