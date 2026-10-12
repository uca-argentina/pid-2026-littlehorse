using DrinkIt.Application.Common;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Nights;

/// <summary>
/// The write side of the bars' stock (US-37). A repository because there are
/// invariants: a bar never holds less than nothing while it keeps selling, and
/// the bars never hold more than the storeroom had to give.
/// </summary>
public interface INightStockRepository
{
    /// <summary>
    /// Records the adjustment and adds its change to what the bar holds for the
    /// night, in the database itself and not as a total worked out in memory: a
    /// sale made since the screen opened is kept. Both or neither.
    /// </summary>
    /// <returns>
    /// The adjustment, or, with nothing written, <see cref="NightErrors.StoreroomShort"/>
    /// when the storeroom at <paramref name="now"/> holds less than it loads, or
    /// <c>ProductErrors.StockMoved</c> when the bar holds less than it takes out.
    /// </returns>
    Task<Result<NightStockAdjustment>> SaveAdjustmentAsync(NightStockAdjustment adjustment, DateTimeOffset now, CancellationToken cancellationToken);
}
