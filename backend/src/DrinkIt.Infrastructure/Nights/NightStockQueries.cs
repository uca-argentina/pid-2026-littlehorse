using DrinkIt.Application.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Nights;

/// <summary>
/// Read side of a night's stock (US-37), bar by bar: sums of its adjustments
/// and of its orders, and the storeroom as it is right now. No venue in any
/// WHERE: the global query filters add it to every table here.
/// </summary>
internal sealed class NightStockQueries(DrinkItDbContext context, TimeProvider clock) : INightStockQueries
{
    public async Task<NightStockSheet?> GetAsync(Guid nightId, CancellationToken cancellationToken)
    {
        List<Guid>? crew = await context.Nights
            .AsNoTracking()
            .Where(candidate => candidate.Id == nightId)
            .Select(candidate => EF.Property<List<Guid>>(candidate, "_crewIds"))
            .FirstOrDefaultAsync(cancellationToken);

        if (crew is null) return null;

        // The KDS of its crew, deactivated ones included: a finished night's
        // record has to add up whoever worked it.
        var bars = await context.StaffUsers
            .AsNoTracking()
            .Where(user => crew.Contains(user.Id) && user.Role == StaffRole.Kds)
            .OrderBy(user => user.Username)
            .Select(user => new { user.Id, user.Username })
            .ToListAsync(cancellationToken);

        List<Movement> loaded = await context.NightStockAdjustments
            .AsNoTracking()
            .Where(adjustment => adjustment.NightId == nightId)
            .GroupBy(adjustment => new { adjustment.KdsId, adjustment.ProductId })
            .Select(group => new Movement(group.Key.KdsId, group.Key.ProductId, group.Sum(adjustment => adjustment.Change)))
            .ToListAsync(cancellationToken);

        // A canceled order gave its units back, so it sold nothing.
        List<Movement> sold = await context.Orders
            .AsNoTracking()
            .Where(order => order.NightId == nightId && order.KdsId != null && order.Status != OrderStatus.Canceled)
            .SelectMany(order => order.Items, (order, item) => new { KdsId = order.KdsId!.Value, item.ProductId, item.Quantity })
            .GroupBy(line => new { line.KdsId, line.ProductId })
            .Select(group => new Movement(group.Key.KdsId, group.Key.ProductId, group.Sum(line => line.Quantity)))
            .ToListAsync(cancellationToken);

        DateTimeOffset now = clock.GetUtcNow();
        Dictionary<Guid, int> heldInBars = await Storeroom.HeldInBarsAsync(context, now, cancellationToken);

        HashSet<Guid> movedHere = [.. loaded.Concat(sold).Select(movement => movement.ProductId)];

        // What the menu sells, plus anything deleted since that this night
        // still moved: its record has to add up.
        var products = await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive || movedHere.Contains(product.Id))
            .OrderBy(product => product.Name)
            .Select(product => new { product.Id, product.Name, product.Stock })
            .ToListAsync(cancellationToken);

        return new NightStockSheet(nightId, [.. bars.Select(bar => new NightStockBar(
            bar.Id,
            bar.Username,
            [.. products.Select(product => new NightStockLine(
                product.Id,
                product.Name,
                Loaded: Units(loaded, bar.Id, product.Id),
                Sold: Units(sold, bar.Id, product.Id),
                InStoreroom: product.Stock - heldInBars.GetValueOrDefault(product.Id)))]))]);

        static int Units(List<Movement> movements, Guid kdsId, Guid productId) => movements
            .Where(movement => movement.KdsId == kdsId && movement.ProductId == productId)
            .Sum(movement => movement.Units);
    }

    private sealed record Movement(Guid KdsId, Guid ProductId, int Units);
}
