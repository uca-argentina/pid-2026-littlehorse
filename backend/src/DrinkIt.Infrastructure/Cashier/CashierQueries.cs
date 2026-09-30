using System.Linq.Expressions;
using DrinkIt.Application.Cashier;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Cashier;

internal sealed class CashierQueries(DrinkItDbContext context) : ICashierQueries
{
    /// <summary>One projection for both questions, so the list and the lookup never disagree.</summary>
    private static readonly Expression<Func<Order, CashierOrder>> AsTheTillSeesIt = order => new CashierOrder(
        order.Code.Value,
        order.CustomerName,
        order.Status,
        order.Items.Sum(item => item.UnitPrice * item.Quantity),
        order.CreatedAt,
        order.PaidAt,
        order.Items.Select(item => new CashierOrderItem(item.ProductName, item.Quantity, item.Note, item.UnitPrice)).ToList());

    // The venue filter scopes both of these on its own — nothing here asks for one.
    public async Task<IReadOnlyList<CashierOrder>> GetAwaitingPaymentAsync(CancellationToken cancellationToken) =>
        await context.Orders
            .AsNoTracking()
            // Only cash: an order waiting for Mercado Pago is not the till's to collect.
            .Where(order => order.Status == OrderStatus.AwaitingPayment && order.Method == PaymentMethod.Cash)
            .OrderBy(order => order.CreatedAt)
            .Select(AsTheTillSeesIt)
            .ToListAsync(cancellationToken);

    public async Task<CashierOrder?> FindAsync(string code, CancellationToken cancellationToken)
    {
        // Something typed at the till that is not a code is simply not an order.
        if (!OrderCode.TryParse(code, out OrderCode? wanted)) return null;

        // A cart never reaches the database, so every row here is somewhere the
        // till can name.
        return await context.Orders
            .AsNoTracking()
            .Where(order => order.Code == wanted)
            .Select(AsTheTillSeesIt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CashierOrder?> FindByTokenAsync(string token, CancellationToken cancellationToken)
    {
        // Whatever a reader typed that is not a token is simply not an order.
        if (!TrackingToken.TryParse(token.Trim(), out TrackingToken? wanted)) return null;

        return await context.Orders
            .AsNoTracking()
            .Where(order => order.TrackingToken == wanted)
            .Select(AsTheTillSeesIt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CashierOrder>> GetCollectedByAsync(
        string cashier,
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
        await context.Orders
            .AsNoTracking()
            .Where(order => order.CollectedBy == cashier && order.PaidAt >= since)
            .OrderByDescending(order => order.PaidAt)
            .Select(AsTheTillSeesIt)
            .ToListAsync(cancellationToken);
}
