using DrinkIt.Application.Tables;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Api.Tests.Features.Tables;

/// <summary>The table port for the endpoint tests: answers, and remembers nothing else.</summary>
internal sealed class FakeTables(int? taken = null, Table? existing = null) : ITableRepository
{
    public Task<bool> NumberExistsAsync(int number, CancellationToken cancellationToken) =>
        Task.FromResult(number == taken);

    public Task<Table?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(existing is not null && existing.Id == id ? existing : null);

    public Task<IReadOnlyList<Table>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Table>>([]);

    public Task<bool> TryAddAsync(Table table, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeTableQueries(params TableSummary[] stored) : ITableQueries
{
    public Task<IReadOnlyList<TableSummary>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TableSummary>>(stored);
}
