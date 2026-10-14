using DrinkIt.Application.Tables;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tests.Tables;

/// <summary>
/// The table port for the tests that only need it to answer: which number is
/// taken, which table it hands back, what was added and whether it saved.
/// </summary>
internal sealed class FakeTables(int? taken = null, Table? existing = null) : ITableRepository
{
    public Table? Added { get; private set; }

    public int Saves { get; private set; }

    public Task<bool> NumberExistsAsync(int number, CancellationToken cancellationToken) =>
        Task.FromResult(number == taken);

    public Task<Table?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(existing is not null && existing.Id == id ? existing : null);

    public Task AddAsync(Table table, CancellationToken cancellationToken)
    {
        Added = table;

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;

        return Task.CompletedTask;
    }
}
