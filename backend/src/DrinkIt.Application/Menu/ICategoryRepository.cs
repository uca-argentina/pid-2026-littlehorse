using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>
/// The write side of the category aggregate. A repository exists here because
/// there is an invariant to protect: a name is unique inside one venue, and the
/// check plus the insert have to agree on the same venue.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>
    /// Whether the venue of the current request already has a category with that
    /// name, ignoring case. The venue is not a parameter on purpose: the global
    /// query filter scopes this to the tenant the request resolved. The name
    /// arrives already trimmed.
    /// </summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the venue of the current request has a category with that id.
    /// Another venue's category answers false, same as one that never existed.
    /// </summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Persists the new category. Saving is the repository's job, not the caller's.</summary>
    Task AddAsync(Category category, CancellationToken cancellationToken);
}
