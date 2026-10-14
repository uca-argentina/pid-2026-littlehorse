using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;

namespace DrinkIt.Application.Metrics;

/// <summary>
/// A night's report (US-47 to US-49): its sales beside its stock, which says
/// what each product was loaded with and which bars the night had. Read-only,
/// and for a night that is still going it is the report so far.
/// </summary>
public sealed class GetNightMetricsHandler(INightMetricsQueries sales, INightStockQueries stock)
{
    public async Task<Result<NightMetrics>> HandleAsync(Guid nightId, CancellationToken cancellationToken)
    {
        NightSales? paid = await sales.GetAsync(nightId, cancellationToken);

        if (paid is null) return NightErrors.NotFound;

        NightStockSheet? sheet = await stock.GetAsync(nightId, cancellationToken);

        if (sheet is null) return NightErrors.NotFound;

        return NightMetrics.Build(paid, sheet);
    }
}
