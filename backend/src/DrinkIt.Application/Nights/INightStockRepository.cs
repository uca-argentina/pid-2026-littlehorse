using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Nights;

/// <summary>
/// The write side of the bars' stock (US-37). A repository because there is an
/// invariant: a bar never holds less than nothing, while it keeps selling.
/// </summary>
public interface INightStockRepository
{
    /// <summary>
    /// Records the adjustment and adds its change to what the bar holds, in the
    /// database itself and not as a total worked out in memory: a sale made
    /// since the screen opened is kept. Both or neither. False, and nothing
    /// written, when the bar holds less than the change takes away.
    /// </summary>
    Task<bool> SaveAdjustmentAsync(NightStockAdjustment adjustment, CancellationToken cancellationToken);
}
