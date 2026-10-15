using DrinkIt.Application.Kds;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Kds;

internal sealed class KdsBoardNotifier(IHubContext<KdsHub> hub) : IKdsBoardNotifier
{
    public Task NotifyBoardChangedAsync(Guid venueId, Guid kdsId, CancellationToken cancellationToken) =>
        hub.Clients.Group(KdsHub.GroupFor(venueId, kdsId)).SendAsync(KdsHub.BoardChanged, cancellationToken);
}
