namespace DrinkIt.Application.Waiter;

/// <summary>
/// Tells the venue's waiters that what they see changed (US-43), so the screen
/// reloads on its own. Implemented over SignalR in Infrastructure.
/// </summary>
public interface IWaiterBoardNotifier
{
    Task NotifyBoardChangedAsync(Guid venueId, CancellationToken cancellationToken);
}
