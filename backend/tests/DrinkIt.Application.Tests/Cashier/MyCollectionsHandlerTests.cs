using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;

namespace DrinkIt.Application.Tests.Cashier;

/// <summary>
/// "Cobros de tu turno": what the signed-in cashier took during the shift. A
/// venue's night crosses midnight, so the shift is the last hours and not today.
/// </summary>
public class MyCollectionsHandlerTests
{
    private static readonly DateTimeOffset Tonight = new(2026, 9, 28, 1, 12, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Always_AsksForWhatTheSignedInCashierTookDuringTheShift()
    {
        Spy queries = new();

        await new MyCollectionsHandler(queries, new TheCashierIs("laura.caja"), new FixedClock(Tonight))
            .HandleAsync(CancellationToken.None);

        Assert.Equal("laura.caja", queries.Cashier);
        Assert.Equal(Tonight - MyCollectionsHandler.Shift, queries.Since);
    }

    private sealed class Spy : ICashierQueries
    {
        public string? Cashier { get; private set; }

        public DateTimeOffset? Since { get; private set; }

        public Task<IReadOnlyList<CashierOrder>> GetCollectedByAsync(string cashier, DateTimeOffset since, CancellationToken cancellationToken)
        {
            Cashier = cashier;
            Since = since;

            return Task.FromResult<IReadOnlyList<CashierOrder>>([]);
        }

        public Task<IReadOnlyList<CashierOrder>> GetAwaitingPaymentAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CashierOrder?> FindAsync(string code, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CashierOrder?> FindByTokenAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TheCashierIs(string username) : ICurrentStaffUser
    {
        public string? Username => username;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
