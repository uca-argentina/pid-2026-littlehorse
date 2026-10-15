using DrinkIt.Application.Metrics;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Metrics;

/// <summary>
/// Read side of a night's report (US-47 to US-49): its bars, its paid orders
/// and its products, projected straight from the DbContext. No venue in any
/// WHERE: the global query filters add it to every table here.
/// </summary>
internal sealed class NightMetricsQueries(DrinkItDbContext context) : INightMetricsQueries
{
    public async Task<NightSales?> GetAsync(Guid nightId, CancellationToken cancellationToken)
    {
        List<Guid>? crew = await context.Nights
            .AsNoTracking()
            .Where(candidate => candidate.Id == nightId)
            .Select(candidate => EF.Property<List<Guid>>(candidate, "_crewIds"))
            .FirstOrDefaultAsync(cancellationToken);

        if (crew is null) return null;

        // The KDS of its crew, deactivated ones included, as in the night's
        // stock: a finished night's report has to show whoever worked it.
        List<NightBar> bars = await context.StaffUsers
            .AsNoTracking()
            .Where(user => crew.Contains(user.Id) && user.Role == StaffRole.Kds)
            .OrderBy(user => user.Username)
            .Select(user => new NightBar(user.Id, user.DisplayName ?? user.Username))
            .ToListAsync(cancellationToken);

        // Sold is paid (US-48): an order only canceled before it was paid, so
        // PaidAt alone leaves out cash nobody collected and every canceled one.
        IQueryable<Order> paid = context.Orders
            .AsNoTracking()
            .Where(order => order.NightId == nightId && order.PaidAt != null);

        List<PaidOrder> orders = await paid
            .Select(order => new PaidOrder(
                order.KdsId,
                order.Method!.Value,
                order.Items.Sum(item => item.UnitPrice * item.Quantity),
                order.Status,
                order.PaidAt!.Value,
                order.ReadyAt,
                order.DeliveredAt,
                order.BalanceAmount))
            .ToListAsync(cancellationToken);

        // From the order lines, not the menu: the name and price each was
        // ordered at, so a product renamed or removed since still adds up.
        List<ProductSales> products = await paid
            .SelectMany(order => order.Items)
            .GroupBy(item => item.ProductId)
            .Select(group => new ProductSales(
                group.Key,
                group.Max(item => item.ProductName)!,
                group.Sum(item => item.Quantity),
                group.Sum(item => item.UnitPrice * item.Quantity)))
            .ToListAsync(cancellationToken);

        return new NightSales(bars, orders, products);
    }
}
