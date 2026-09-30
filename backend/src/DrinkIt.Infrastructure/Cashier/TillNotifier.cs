using DrinkIt.Application.Cashier;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Cashier;

internal sealed class TillNotifier(IHubContext<TillHub> hub) : ITillNotifier
{
    public Task NotifyTillChangedAsync(Guid venueId, CancellationToken cancellationToken) =>
        hub.Clients.Group(TillHub.GroupFor(venueId)).SendAsync(TillHub.TillChanged, cancellationToken);
}
