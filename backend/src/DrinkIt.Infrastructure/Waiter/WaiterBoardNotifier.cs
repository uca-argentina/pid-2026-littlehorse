using DrinkIt.Application.Waiter;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Waiter;

internal sealed class WaiterBoardNotifier(IHubContext<WaiterHub> hub) : IWaiterBoardNotifier
{
    public Task NotifyBoardChangedAsync(Guid venueId, CancellationToken cancellationToken) =>
        hub.Clients.Group(WaiterHub.GroupFor(venueId)).SendAsync(WaiterHub.TablesChanged, cancellationToken);
}
