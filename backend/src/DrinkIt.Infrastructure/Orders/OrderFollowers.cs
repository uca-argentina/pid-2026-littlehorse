using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.SignalR;

namespace DrinkIt.Infrastructure.Orders;

internal sealed class OrderFollowers(IHubContext<TrackingHub> hub) : IOrderFollowers
{
    public Task NotifyOrderChangedAsync(TrackingToken order, CancellationToken cancellationToken) =>
        hub.Clients.Group(TrackingHub.GroupFor(order)).SendAsync(TrackingHub.OrderChanged, cancellationToken);
}
