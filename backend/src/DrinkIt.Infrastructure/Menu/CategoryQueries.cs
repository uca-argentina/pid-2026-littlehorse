using DrinkIt.Application.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Menu;

/// <summary>Read side: no repository, no tracking, a projection straight to the DTO.</summary>
internal sealed class CategoryQueries(DrinkItDbContext context) : ICategoryQueries
{
    public async Task<IReadOnlyList<CategoryListItem>> ListAsync(CancellationToken cancellationToken) =>
        await context.Categories
            .AsNoTracking()
            // The moment and not the id: a uniqueidentifier does not sort in the
            // order it was made, and this is the order the customer's tabs take.
            .OrderBy(category => category.CreatedAt)
            .ThenBy(category => category.Name)
            .Select(category => new CategoryListItem(category.Id, category.Name))
            .ToListAsync(cancellationToken);
}
