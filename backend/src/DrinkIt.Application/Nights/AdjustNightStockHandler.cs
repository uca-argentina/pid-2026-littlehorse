using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Nights;

/// <summary>The units to load into one bar for the night, or to take out of it when they were loaded wrong.</summary>
public sealed record AdjustNightStockCommand(Guid NightId, Guid KdsId, Guid ProductId, int Change);

/// <summary>
/// US-37: the administrator loads each bar's stock for the night, product by
/// product. A bar is a KDS of the night's crew. The change is what gets saved,
/// never the total on the screen: the bar keeps selling while it is open, and
/// a total typed over it would put back drinks already sold.
/// </summary>
public sealed class AdjustNightStockHandler(
    INightRepository nights,
    IProductRepository products,
    IStaffUserRepository staffUsers,
    INightStockRepository shelves,
    INightStockQueries sheets,
    TimeProvider clock)
{
    public async Task<Result<NightStockSheet>> HandleAsync(AdjustNightStockCommand command, CancellationToken cancellationToken)
    {
        Night? night = await nights.GetForUpdateAsync(command.NightId, cancellationToken);

        if (night is null) return NightErrors.NotFound;

        DateTimeOffset now = clock.GetUtcNow();

        // Expected from a screen that was opened a while ago, so results and
        // not the domain's exceptions.
        if (night.HasEndedAt(now)) return NightErrors.StockClosed;
        if (await nights.HasUnfinishedBeforeAsync(night.StartsAt, now, cancellationToken)) return NightErrors.EarlierNightOpen;

        StaffUser? bar = await staffUsers.GetForUpdateAsync(command.KdsId, cancellationToken);

        if (bar is null) return NightErrors.BarNotFound;
        if (!night.IsWorkedBy(bar.Id)) return NightErrors.BarNotInNight;

        Product? product = await products.GetForUpdateAsync(command.ProductId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;

        // What is left can only be a malformed request (a change of zero, an
        // account that is not a KDS): the domain throws and the API answers 400.
        NightStockAdjustment adjustment = night.AdjustStock(bar, product, command.Change, now);

        // Sales since the screen opened leaving the bar too little is expected.
        if (!await shelves.SaveAdjustmentAsync(adjustment, cancellationToken)) return ProductErrors.StockMoved;

        return (await sheets.GetAsync(night.Id, cancellationToken))!;
    }
}
