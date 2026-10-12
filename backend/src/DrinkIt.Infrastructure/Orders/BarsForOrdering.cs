using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Orders;

/// <summary>
/// Read-only, like ProductsForOrdering: what each bar of the night holds and
/// how much work it has. The stock is taken by OrderRepository.AddAsync. No
/// venue in any WHERE: the global query filters add it.
/// </summary>
internal sealed class BarsForOrdering(DrinkItDbContext context) : IBarsForOrdering
{
    public async Task<IReadOnlyList<BarSupply>> GetAsync(Guid nightId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        List<Guid> crew = await context.Nights
            .AsNoTracking()
            .Where(night => night.Id == nightId)
            .Select(night => EF.Property<List<Guid>>(night, "_crewIds"))
            .FirstOrDefaultAsync(cancellationToken) ?? [];

        // By name, so equal work always goes to the same one first.
        List<Guid> bars = await context.StaffUsers
            .AsNoTracking()
            .Where(user => crew.Contains(user.Id) && user.Role == StaffRole.Kds && user.IsActive)
            .OrderBy(user => user.Username)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, int> loads = await context.Orders
            .AsNoTracking()
            .Where(order => order.KdsId != null && bars.Contains(order.KdsId.Value)
                && (order.Status == OrderStatus.Queued || order.Status == OrderStatus.InPreparation))
            .GroupBy(order => order.KdsId!.Value)
            .Select(group => new { Bar = group.Key, Orders = group.Count() })
            .ToDictionaryAsync(load => load.Bar, load => load.Orders, cancellationToken);

        var units = await context.BarStocks
            .AsNoTracking()
            .Where(stock => bars.Contains(stock.KdsId) && productIds.Contains(stock.ProductId))
            .Select(stock => new { stock.KdsId, stock.ProductId, stock.Units })
            .ToListAsync(cancellationToken);

        return [.. bars.Select(bar => new BarSupply(
            bar,
            loads.GetValueOrDefault(bar),
            units.Where(stock => stock.KdsId == bar).ToDictionary(stock => stock.ProductId, stock => stock.Units)))];
    }
}
