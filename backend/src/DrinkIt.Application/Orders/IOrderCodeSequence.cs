using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// Hands out the next order code for the venue of this request.
/// </summary>
/// <remarks>
/// A port and not a calculation, because the only place that can answer it
/// without two simultaneous orders getting the same code is the database. That
/// is criterion 4 of US-11, and it cannot be met in memory.
/// </remarks>
public interface IOrderCodeSequence
{
    Task<OrderCode> NextAsync(CancellationToken cancellationToken);
}
