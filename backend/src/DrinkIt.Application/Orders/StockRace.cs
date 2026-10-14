using DrinkIt.Application.Common;

namespace DrinkIt.Application.Orders;

/// <summary>
/// Going round again when somebody else took the same drink between reading
/// the stock and writing it (<see cref="OrderErrors.StockMoved"/>). Confirming
/// an order and collecting one at the till both lose that race in a packed
/// venue, and both answer it the same way.
/// </summary>
internal static class StockRace
{
    /// <summary>
    /// How many times. Everybody orders the same three drinks at once, so
    /// losing a race is ordinary and must not reach anybody: what must reach
    /// them is running out, which is a different answer.
    /// </summary>
    public const int Attempts = 5;

    /// <summary>
    /// The first outcome that is not a lost race. When every attempt loses,
    /// the last one: somebody is hammering the same drink, and the honest
    /// answer is to look again rather than keep trying.
    /// </summary>
    public static async Task<Result<T>> RunAsync<T>(Func<Task<Result<T>>> attempt)
    {
        Result<T> outcome = OrderErrors.StockMoved;

        for (int tried = 0; tried < Attempts; tried++)
        {
            outcome = await attempt();

            if (outcome.IsSuccess || outcome.Error!.Code != OrderErrors.StockMoved.Code) return outcome;
        }

        return outcome;
    }
}
