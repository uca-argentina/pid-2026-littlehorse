using DrinkIt.Application.Common;

namespace DrinkIt.Application.Cashier;

/// <summary>
/// "Cobros de tu turno" (US-26): what the signed-in cashier took during the shift.
/// </summary>
/// <remarks>
/// The shift is the last twelve hours and not "today": a venue's night crosses
/// midnight, and at 01:00 "today" would forget everything taken since 22:00.
/// </remarks>
// ponytail: fixed window, not a real shift; a Shift entity with open/close if the till ever closes out cash.
public sealed class MyCollectionsHandler(ICashierQueries queries, ICurrentStaffUser cashier, TimeProvider clock)
{
    public static readonly TimeSpan Shift = TimeSpan.FromHours(12);

    public Task<IReadOnlyList<CashierOrder>> HandleAsync(CancellationToken cancellationToken) =>
        queries.GetCollectedByAsync(cashier.Username ?? string.Empty, clock.GetUtcNow() - Shift, cancellationToken);
}
