using DrinkIt.Application.Common;
using DrinkIt.Domain.Common;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.ArchitectureTests;

/// <summary>
/// Builds the EF model without touching a database, so these run in the fast
/// loop alongside the unit tests.
/// </summary>
public class PersistenceRuleTests
{
    [Fact]
    public void EveryVenueScopedEntity_WhenBuildingTheModel_HasAGlobalQueryFilter()
    {
        using DrinkItDbContext context = BuildModelOnlyContext();

        string[] unfiltered =
        [
            .. context.Model.GetEntityTypes()
                .Where(entity => typeof(IBelongsToVenue).IsAssignableFrom(entity.ClrType))
                .Where(entity => entity.GetDeclaredQueryFilters().Count == 0)
                .Select(entity => entity.ClrType.Name)
                .Order(StringComparer.Ordinal)
        ];

        Assert.True(
            unfiltered.Length == 0,
            $"These types belong to a venue but have no global query filter, so a query could "
            + $"return another venue's rows: {string.Join(", ", unfiltered)}");
    }

    private static DrinkItDbContext BuildModelOnlyContext()
    {
        DbContextOptions<DrinkItDbContext> options = new DbContextOptionsBuilder<DrinkItDbContext>()
            .UseSqlServer("Server=model-only;Database=none")
            .Options;

        return new DrinkItDbContext(options, new UnresolvedVenue());
    }

    private sealed class UnresolvedVenue : ICurrentVenue
    {
        public Guid Id => Guid.Empty;
    }
}
