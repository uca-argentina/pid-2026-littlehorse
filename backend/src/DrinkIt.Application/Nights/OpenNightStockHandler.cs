using DrinkIt.Application.Common;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Nights;

/// <summary>One product of a night's stock, as the administration reads it.</summary>
public sealed record NightStockLine(Guid ProductId, string ProductName, int Loaded, int Sold, int Remaining);

/// <summary>
/// US-37: the stock of a night, opened the first time somebody needs it. Each
/// product starts with what the latest earlier night left of it, or with the
/// number it was created with when no earlier night had it, and the rows are
/// not made until the night before is over: until then nobody knows what it
/// will leave.
/// </summary>
/// <remarks>
/// Opening only adds what is missing, so asking again — or asking after a new
/// product was made — gives the same answer for what already exists.
/// </remarks>
public sealed class OpenNightStockHandler(
    INightStockRepository stocks,
    ICurrentVenue currentVenue,
    TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<NightStockLine>>> HandleAsync(Guid nightId, CancellationToken cancellationToken)
    {
        NightForStock? night = await stocks.FindNightAsync(nightId, cancellationToken);

        if (night is null) return NightErrors.NotFound;

        // The end is exclusive, so a night that ends at 06:00 is over at 06:00.
        if (night.PreviousEndedAt > clock.GetUtcNow()) return NightErrors.PreviousNightNotOver;

        List<NightStock> rows = [.. await stocks.ListAsync(nightId, cancellationToken)];
        IReadOnlyList<StockedProduct> products = await stocks.ListActiveProductsAsync(cancellationToken);

        List<StockedProduct> missing = [.. products.Where(product => rows.TrueForAll(row => row.ProductId != product.Id))];

        if (missing.Count > 0)
        {
            IReadOnlyDictionary<Guid, int> carried = await stocks.CarriedOverAsync(nightId, cancellationToken);

            List<NightStock> opened = [.. missing.Select(product =>
                NightStock.Open(currentVenue.Id, nightId, product.Id, carried.GetValueOrDefault(product.Id, product.InitialStock)))];

            await stocks.AddRangeAsync(opened, cancellationToken);

            rows.AddRange(opened);
        }

        // In the order of the menu's own listing, so the screen does not
        // shuffle between one opening and the next.
        return products
            .Select(product => (product, row: rows.Find(candidate => candidate.ProductId == product.Id)))
            .Where(pair => pair.row is not null)
            .Select(pair => new NightStockLine(pair.product.Id, pair.product.Name, pair.row!.Loaded, pair.row.Sold, pair.row.Remaining))
            .ToList();
    }
}
