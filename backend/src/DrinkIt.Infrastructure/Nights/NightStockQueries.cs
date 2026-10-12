using DrinkIt.Application.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Nights;

/// <summary>
/// Read side of a night's stock (US-37): sums of its adjustments and of its
/// orders, and of every night after it, so <see cref="NightStockLine.From"/>
/// can work back from the shelf. No venue in any WHERE: the global query
/// filters add it to every table read here.
/// </summary>
internal sealed class NightStockQueries(DrinkItDbContext context) : INightStockQueries
{
    public async Task<NightStockSheet?> GetAsync(Guid nightId, CancellationToken cancellationToken)
    {
        DateTimeOffset? startsAt = await context.Nights
            .AsNoTracking()
            .Where(night => night.Id == nightId)
            .Select(night => (DateTimeOffset?)night.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (startsAt is null) return null;

        // This night and every one after it: what they moved is what separates
        // the shelf from what this one started with.
        List<Guid> fromHereOn = await context.Nights
            .AsNoTracking()
            .Where(night => night.StartsAt >= startsAt)
            .Select(night => night.Id)
            .ToListAsync(cancellationToken);

        List<Movement> added = await context.NightStockAdjustments
            .AsNoTracking()
            .Where(adjustment => fromHereOn.Contains(adjustment.NightId))
            .GroupBy(adjustment => new { adjustment.NightId, adjustment.ProductId })
            .Select(group => new Movement(group.Key.NightId, group.Key.ProductId, group.Sum(adjustment => adjustment.Change)))
            .ToListAsync(cancellationToken);

        // A canceled order gave its units back, so it sold nothing.
        List<Movement> sold = await context.Orders
            .AsNoTracking()
            .Where(order => order.NightId != null && fromHereOn.Contains(order.NightId.Value) && order.Status != OrderStatus.Canceled)
            .SelectMany(order => order.Items, (order, item) => new { NightId = order.NightId!.Value, item.ProductId, item.Quantity })
            .GroupBy(line => new { line.NightId, line.ProductId })
            .Select(group => new Movement(group.Key.NightId, group.Key.ProductId, group.Sum(line => line.Quantity)))
            .ToListAsync(cancellationToken);

        HashSet<Guid> movedHere = [.. added.Concat(sold).Where(movement => movement.NightId == nightId).Select(movement => movement.ProductId)];

        // What the menu sells, plus anything deleted since that this night
        // still moved: its record has to add up.
        var products = await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive || movedHere.Contains(product.Id))
            .OrderBy(product => product.Name)
            .Select(product => new { product.Id, product.Name, product.Stock })
            .ToListAsync(cancellationToken);

        List<NightStockLine> lines = products
            .Select(product => NightStockLine.From(
                product.Id,
                product.Name,
                shelf: product.Stock,
                added: Units(added, product.Id, here: true),
                sold: Units(sold, product.Id, here: true),
                netOfLaterNights: Units(added, product.Id, here: false) - Units(sold, product.Id, here: false)))
            .ToList();

        return new NightStockSheet(nightId, lines);

        int Units(List<Movement> movements, Guid productId, bool here) => movements
            .Where(movement => movement.ProductId == productId && (movement.NightId == nightId) == here)
            .Sum(movement => movement.Units);
    }

    private sealed record Movement(Guid NightId, Guid ProductId, int Units);
}
