using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Orders;

internal sealed class OrderTrackingQueries(DrinkItDbContext context) : IOrderTrackingQueries
{
    public async Task<TrackedOrder?> FindAsync(
        string code,
        string? token,
        CancellationToken cancellationToken)
    {
        // Read before the query rather than inside it: what arrives here is a
        // path segment somebody typed, and a code that is not a code is a link
        // leading nowhere — the same answer as a code nobody has — not an
        // exception on the way to asking the database.
        if (!OrderCode.TryParse(code, out OrderCode? wanted)) return null;

        // The venue filter scopes this, so the same code in another bar is
        // simply not here. Read-only and projected straight to the shape the
        // screen draws: this runs every three seconds on every phone in a
        // packed venue.
        var found = await context.Orders
            .AsNoTracking()
            .Where(order => order.Code == wanted)
            .Select(order => new
            {
                order.Code,
                order.CustomerName,
                order.Status,
                order.PaidAt,
                order.TrackingToken,
                Items = order.Items
                    .Select(item => new TrackedOrderItem(item.ProductName, item.Quantity, item.Note))
                    .ToList(),
                Total = order.Items.Sum(item => item.UnitPrice * item.Quantity),
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Every way of not getting in answers the same. The token is compared
        // after the row is read and in constant time, so nothing about how long
        // this took says how close a guess was.
        if (found is null || !found.TrackingToken.Matches(token) || found.Status.IsFinished()) return null;

        return new TrackedOrder(
            found.Code.Value,
            found.CustomerName,
            found.Status,
            found.Total,
            found.PaidAt,
            found.Items);
    }
}
