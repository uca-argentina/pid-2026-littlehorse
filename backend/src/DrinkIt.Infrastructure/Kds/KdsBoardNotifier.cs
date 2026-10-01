using DrinkIt.Application.Kds;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Kds;

internal sealed class KdsBoardNotifier(IHubContext<KdsHub> hub) : IKdsBoardNotifier
{
    public Task NotifyBoardChangedAsync(Guid venueId, CancellationToken cancellationToken) =>
        hub.Clients.Group(KdsHub.GroupFor(venueId)).SendAsync(KdsHub.BoardChanged, cancellationToken);
}
