using DrinkIt.Application.Common;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Kds;

/// <summary>
/// Writes a tablet's beat (US-38): one row per bar, moved forward every time.
/// Read back by BarsForOrdering when an order is paid. No venue in any WHERE:
/// the global query filter adds it, and a new row takes the current one.
/// </summary>
public sealed class KdsBeats(DrinkItDbContext context, ICurrentVenue currentVenue)
{
    public async Task RecordAsync(Guid kdsId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (await MoveForwardAsync(kdsId, at, cancellationToken)) return;

        context.KdsBeats.Add(new KdsBeat { VenueId = currentVenue.Id, KdsId = kdsId, LastBeatAt = at });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same tablet's first beat from another connection made the
            // row in between. It is there now, so this beat only moves it.
            context.ChangeTracker.Clear();
            await MoveForwardAsync(kdsId, at, cancellationToken);
        }
    }

    private async Task<bool> MoveForwardAsync(Guid kdsId, DateTimeOffset at, CancellationToken cancellationToken) =>
        await context.KdsBeats
            .Where(beat => beat.KdsId == kdsId)
            .ExecuteUpdateAsync(set => set.SetProperty(beat => beat.LastBeatAt, at), cancellationToken) > 0;
}
