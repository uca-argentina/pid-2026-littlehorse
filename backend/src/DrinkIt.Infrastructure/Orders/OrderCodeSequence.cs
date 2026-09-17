using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Orders;

/// <summary>
/// Hands out the next order code of the venue, one row per venue in the
/// database.
/// </summary>
/// <remarks>
/// Criterion 4 of US-11 lives here: two customers confirming at the same
/// instant must not get the same code. The update is conditional on the value
/// that was read — "move this venue from K-4821 to K-4822, but only if it is
/// still at K-4821" — so of two racing attempts exactly one changes a row and
/// the loser reads again and takes the next one. No locks, no sequence object,
/// and nothing that behaves differently under load than it does in a test.
///
/// Every statement here runs on its own, outside the unit of work that saves
/// the order, and deliberately: touching SaveChanges would write the stock the
/// handler has already taken off the shelf before the order that took it
/// exists. The cost is that an order that fails afterwards leaves a gap in the
/// numbering, which is what every sequence in every database does.
/// </remarks>
internal sealed class OrderCodeSequence(DrinkItDbContext context, ICurrentVenue currentVenue)
    : IOrderCodeSequence
{
    /// <summary>
    /// Enough that exhausting them means something else is wrong. Each retry
    /// costs one round trip and only happens when two orders land together.
    /// </summary>
    private const int Attempts = 8;

    public async Task<OrderCode> NextAsync(CancellationToken cancellationToken)
    {
        Guid venueId = currentVenue.Id;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            OrderCodeCounter? counter = await context.OrderCodeCounters
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.VenueId == venueId, cancellationToken);

            if (counter is null)
            {
                if (await StartTheVenueOff(venueId, cancellationToken)) return OrderCode.First;

                // Somebody else started it while we were looking. Read again.
                continue;
            }

            OrderCode next = OrderCode.Parse(counter.LastCode).Next();

            int moved = await context.OrderCodeCounters
                .Where(row => row.VenueId == venueId && row.LastCode == counter.LastCode)
                .ExecuteUpdateAsync(row => row.SetProperty(x => x.LastCode, next.Value), cancellationToken);

            if (moved == 1) return next;
        }

        throw new InvalidOperationException(
            "Could not hand out an order code: the venue's counter kept moving underneath us.");
    }

    /// <summary>
    /// The venue's very first order, written with one statement that inserts
    /// only if nobody has: false means another request got there first. Raw SQL
    /// rather than Add plus SaveChanges, which would also write whatever else
    /// the request has pending — see the note on the class.
    /// </summary>
    private async Task<bool> StartTheVenueOff(Guid venueId, CancellationToken cancellationToken)
    {
        string first = OrderCode.First.Value;

        int inserted = await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO OrderCodeCounters (VenueId, LastCode)
            SELECT {venueId}, {first}
            WHERE NOT EXISTS (SELECT 1 FROM OrderCodeCounters WHERE VenueId = {venueId})
            """,
            cancellationToken);

        return inserted == 1;
    }
}
