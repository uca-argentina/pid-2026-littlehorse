using DrinkIt.Domain.Common;

namespace DrinkIt.Application.Common;

/// <summary>
/// Where a saved aggregate's raised events get reacted to. Whoever saves the
/// aggregate calls this once, after the write actually commits.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}
