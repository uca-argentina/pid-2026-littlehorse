using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>
/// The write side of the product aggregate. A repository exists here because
/// there is an invariant to protect: a name is unique inside one venue, and the
/// check plus the insert have to agree on the same venue.
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Whether the venue of the current request already sells a product with
    /// that name, ignoring case: "gin tonic" and "Gin Tonic" are the same thing
    /// to the customer. The venue is not a parameter on purpose: the global
    /// query filter scopes this to the tenant the request resolved, so no
    /// caller can aim it elsewhere. The name arrives already trimmed.
    /// </summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>Persists the new product. Saving is the repository's job, not the caller's.</summary>
    Task AddAsync(Product product, CancellationToken cancellationToken);
}
