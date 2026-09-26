using DrinkIt.Domain.Menu;
using DrinkIt.Infrastructure.Persistence;

namespace DrinkIt.Api.IntegrationTests.Persistence;

/// <summary>
/// A product points at a category the database has to know, so every test that
/// stores one starts by putting a category of the same venue in the same save.
/// </summary>
internal static class SeedCategory
{
    /// <summary>For the tests that build their products before they open the context.</summary>
    public static Category For(Guid venueId, string name = "Tragos") =>
        Category.Create(venueId, name, DateTimeOffset.UtcNow);

    /// <summary>Tracked and not saved: the caller's own SaveChanges writes it before the products.</summary>
    public static Category For(DrinkItDbContext seed, Guid venueId, string name = "Tragos")
    {
        Category category = For(venueId, name);

        seed.Categories.Add(category);

        return category;
    }
}
