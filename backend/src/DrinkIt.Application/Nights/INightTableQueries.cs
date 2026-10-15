namespace DrinkIt.Application.Nights;

/// <summary>One VIP table a night sells, as its screen shows it (US-40).</summary>
/// <remarks>
/// AccessCode is the secret in the table's QR (§8.2). Only the night's
/// administration reads it, to hand it over.
/// </remarks>
public sealed record NightTableLine(Guid TableId, int Number, int Capacity, string? Location, decimal Budget, decimal Spent, string AccessCode);

/// <summary>The read side of a night's tables. No repository: reads have no invariant to protect.</summary>
public interface INightTableQueries
{
    /// <summary>The night's tables by number, or null when this venue has no such night.</summary>
    Task<IReadOnlyList<NightTableLine>?> ListAsync(Guid nightId, CancellationToken cancellationToken);
}
