using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Api.Tests.Features.Menu;

/// <summary>The category port for the endpoint tests: answers, and remembers nothing else.</summary>
internal sealed class FakeCategories(string? taken = null, bool knowsEveryId = true) : ICategoryRepository
{
    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(string.Equals(name, taken, StringComparison.OrdinalIgnoreCase));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(knowsEveryId);

    public Task AddAsync(Category category, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeCategoryQueries(params CategoryListItem[] stored) : ICategoryQueries
{
    public Task<IReadOnlyList<CategoryListItem>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CategoryListItem>>(stored);
}
