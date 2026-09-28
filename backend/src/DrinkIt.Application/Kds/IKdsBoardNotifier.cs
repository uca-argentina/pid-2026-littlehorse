namespace DrinkIt.Application.Kds;

/// <summary>
/// Tells the bar's board that its queue changed (US-15), so the tablet can
/// reload instead of somebody refreshing it by hand.
/// </summary>
/// <remarks>
/// Implemented over SignalR in Infrastructure — this port is what keeps the
/// domain, and every use case, from knowing that.
/// </remarks>
public interface IKdsBoardNotifier
{
    Task NotifyBoardChangedAsync(Guid venueId, CancellationToken cancellationToken);
}
