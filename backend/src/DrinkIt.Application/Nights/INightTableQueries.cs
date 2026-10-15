namespace DrinkIt.Application.Nights;

/// <summary>One VIP table a night sells, as its screen shows it (US-40).</summary>
public sealed record NightTableLine(Guid TableId, int Number, int Capacity, string? Location, decimal Budget, decimal Spent);

/// <summary>The read side of a night's tables. No repository: reads have no invariant to protect.</summary>
public interface INightTableQueries
{
    /// <summary>The night's tables by number, or null when this venue has no such night.</summary>
    Task<IReadOnlyList<NightTableLine>?> ListAsync(Guid nightId, CancellationToken cancellationToken);
}
