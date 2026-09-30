namespace DrinkIt.Application.Cashier;

/// <summary>
/// Tells the venue's tills that what waits for cash changed (US-26), so "Por
/// cobrar" reloads on its own. Implemented over SignalR in Infrastructure.
/// </summary>
public interface ITillNotifier
{
    Task NotifyTillChangedAsync(Guid venueId, CancellationToken cancellationToken);
}
