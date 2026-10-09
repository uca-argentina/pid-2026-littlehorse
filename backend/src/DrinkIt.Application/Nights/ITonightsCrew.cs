namespace DrinkIt.Application.Nights;

/// <summary>
/// Whether a staff account works the venue's night (US-35, criterion 4): the
/// KDS, the till and the waiter only see orders while it does.
/// </summary>
/// <remarks>
/// The night that counts is the last one that started, not only the one on:
/// paid orders are still made and handed over after closing, so a night's crew
/// keeps its screens until the next night begins.
/// </remarks>
public interface ITonightsCrew
{
    Task<bool> IncludesAsync(Guid staffUserId, DateTimeOffset at, CancellationToken cancellationToken);
}
