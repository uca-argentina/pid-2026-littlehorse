using DrinkIt.Application.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Nights;

/// <summary>
/// Read side of a night's stock (US-37), bar by bar: sums of its adjustments
/// and of its orders, and of every night after it, so
/// <see cref="NightStockLine.From"/> can work back from what each bar holds.
/// No venue in any WHERE: the global query filters add it to every table here.
/// </summary>
internal sealed class NightStockQueries(DrinkItDbContext context) : INightStockQueries
{
    public async Task<NightStockSheet?> GetAsync(Guid nightId, CancellationToken cancellationToken)
    {
        var night = await context.Nights
            .AsNoTracking()
            .Where(candidate => candidate.Id == nightId)
            .Select(candidate => new { candidate.StartsAt, Crew = EF.Property<List<Guid>>(candidate, "_crewIds") })
            .FirstOrDefaultAsync(cancellationToken);

        if (night is null) return null;

        // The KDS of its crew, deactivated ones included: a finished night's
        // record has to add up whoever worked it.
        var bars = await context.StaffUsers
            .AsNoTracking()
            .Where(user => night.Crew.Contains(user.Id) && user.Role == StaffRole.Kds)
            .OrderBy(user => user.Username)
            .Select(user => new { user.Id, user.Username })
            .ToListAsync(cancellationToken);

        List<Guid> barIds = [.. bars.Select(bar => bar.Id)];

        // This night and every one after it: what they moved is what separates
        // what a bar holds now from what it started this one with.
        List<Guid> fromHereOn = await context.Nights
            .AsNoTracking()
            .Where(candidate => candidate.StartsAt >= night.StartsAt)
            .Select(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        List<Movement> added = await context.NightStockAdjustments
            .AsNoTracking()
            .Where(adjustment => fromHereOn.Contains(adjustment.NightId) && barIds.Contains(adjustment.KdsId))
            .GroupBy(adjustment => new { adjustment.NightId, adjustment.KdsId, adjustment.ProductId })
            .Select(group => new Movement(group.Key.NightId, group.Key.KdsId, group.Key.ProductId, group.Sum(adjustment => adjustment.Change)))
            .ToListAsync(cancellationToken);

        // A canceled order gave its units back, so it sold nothing.
        List<Movement> sold = await context.Orders
            .AsNoTracking()
            .Where(order => order.NightId != null && fromHereOn.Contains(order.NightId.Value)
                && order.KdsId != null && barIds.Contains(order.KdsId.Value)
                && order.Status != OrderStatus.Canceled)
            .SelectMany(order => order.Items, (order, item) => new { NightId = order.NightId!.Value, KdsId = order.KdsId!.Value, item.ProductId, item.Quantity })
            .GroupBy(line => new { line.NightId, line.KdsId, line.ProductId })
            .Select(group => new Movement(group.Key.NightId, group.Key.KdsId, group.Key.ProductId, group.Sum(line => line.Quantity)))
            .ToListAsync(cancellationToken);

        var shelves = await context.BarStocks
            .AsNoTracking()
            .Where(stock => barIds.Contains(stock.KdsId))
            .Select(stock => new { stock.KdsId, stock.ProductId, stock.Units })
            .ToListAsync(cancellationToken);

        HashSet<Guid> movedHere = [.. added.Concat(sold).Where(movement => movement.NightId == nightId).Select(movement => movement.ProductId)];

        // What the menu sells, plus anything deleted since that this night
        // still moved: its record has to add up.
        var products = await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive || movedHere.Contains(product.Id))
            .OrderBy(product => product.Name)
            .Select(product => new { product.Id, product.Name })
            .ToListAsync(cancellationToken);

        return new NightStockSheet(nightId, [.. bars.Select(bar => new NightStockBar(
            bar.Id,
            bar.Username,
            [.. products.Select(product => NightStockLine.From(
                product.Id,
                product.Name,
                shelf: shelves.FirstOrDefault(stock => stock.KdsId == bar.Id && stock.ProductId == product.Id)?.Units ?? 0,
                added: Units(added, bar.Id, product.Id, here: true),
                sold: Units(sold, bar.Id, product.Id, here: true),
                netOfLaterNights: Units(added, bar.Id, product.Id, here: false) - Units(sold, bar.Id, product.Id, here: false)))]))]);

        int Units(List<Movement> movements, Guid kdsId, Guid productId, bool here) => movements
            .Where(movement => movement.KdsId == kdsId && movement.ProductId == productId && (movement.NightId == nightId) == here)
            .Sum(movement => movement.Units);
    }

    private sealed record Movement(Guid NightId, Guid KdsId, Guid ProductId, int Units);
}
