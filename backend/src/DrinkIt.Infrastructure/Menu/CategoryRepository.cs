using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Menu;

internal sealed class CategoryRepository(DrinkItDbContext context) : ICategoryRepository
{
    /// <summary>
    /// No venue in the WHERE: the global query filter adds the one the request
    /// resolved. Case is ignored by the column's collation, which is what the
    /// unique index compares with too, so the two cannot disagree.
    /// </summary>
    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        context.Categories.AnyAsync(category => category.Name == name, cancellationToken);

    /// <summary>The venue filter applies here too: another venue's id finds nothing.</summary>
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        context.Categories.AnyAsync(category => category.Id == id, cancellationToken);

    public async Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        context.Categories.Add(category);

        await context.SaveChangesAsync(cancellationToken);
    }
}
