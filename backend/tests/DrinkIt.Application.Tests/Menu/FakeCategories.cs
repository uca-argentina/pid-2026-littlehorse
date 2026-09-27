using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

/// <summary>
/// The category port for the tests that only need it to answer: whether a name
/// is taken, whether an id is one of the venue's, and what was added.
/// </summary>
internal sealed class FakeCategories(string? taken = null, bool knowsEveryId = true) : ICategoryRepository
{
    public Category? Added { get; private set; }

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(string.Equals(name, taken, StringComparison.OrdinalIgnoreCase));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(knowsEveryId);

    public Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        Added = category;

        return Task.CompletedTask;
    }
}
