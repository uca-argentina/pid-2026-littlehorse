using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

public sealed record CreateCategoryCommand(string Name);

/// <summary>The category as the administration screen shows it back after creating it.</summary>
public sealed record CategorySummary(Guid Id, string Name)
{
    internal static CategorySummary Of(Category category) => new(category.Id, category.Name);
}

/// <summary>
/// Adds a category to the menu of the venue the signed-in administrator belongs
/// to. The venue comes from <see cref="ICurrentVenue"/>, which reads the token
/// claim, so an administrator cannot add one anywhere else by changing the request.
/// </summary>
public sealed class CreateCategoryHandler(ICategoryRepository categories, ICurrentVenue currentVenue, TimeProvider clock)
{
    public static readonly Error NameTaken =
        new("category.name_taken", "This venue already has a category with that name.");

    public async Task<Result<CategorySummary>> HandleAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        // Trimmed here, before the availability check, so "Cervezas " cannot get
        // past it and collide on the unique index afterwards, where the failure
        // is a 500 and not a readable message.
        string name = (command.Name ?? string.Empty).Trim();

        if (await categories.NameExistsAsync(name, cancellationToken)) return NameTaken;

        // Whatever is left wrong with the data is a broken domain invariant, and
        // Create throws. The API turns that into a 400.
        Category category = Category.Create(currentVenue.Id, name, clock.GetUtcNow());

        await categories.AddAsync(category, cancellationToken);

        return CategorySummary.Of(category);
    }
}
