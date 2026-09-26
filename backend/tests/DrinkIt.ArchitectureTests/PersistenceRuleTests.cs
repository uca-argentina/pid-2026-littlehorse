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

    /// <summary>
    /// US-30. The tables that exist today, and this test is what makes a new one
    /// answer the question instead of being forgotten: it either carries a
    /// creation time or is named here with the reason it does not.
    /// </summary>
    [Fact]
    public void EveryTable_WhenBuildingTheModel_HasACreationTimeOrIsOnTheAllowList()
    {
        // OrderItem is written once with its order and never changes after it,
        // so the order's own time is its time. The counter is technical: it is
        // written on every sale and no user ever edits it.
        string[] withoutOne = ["OrderItem", "OrderCodeCounter"];

        using DrinkItDbContext context = BuildModelOnlyContext();

        string[] missing =
        [
            .. context.Model.GetEntityTypes()
                .Where(entity => !entity.IsOwned())
                .Where(entity => entity.FindProperty("CreatedAt") is null)
                .Select(entity => entity.ClrType.Name)
                .Except(withoutOne)
                .Order(StringComparer.Ordinal)
        ];

        Assert.True(
            missing.Length == 0,
            "These tables have no CreatedAt, so nobody can tell when a row was made. Add the "
            + $"audit columns, or list the table with its reason: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Who modified something only makes sense where an administrator edits it.
    /// Everything that carries the mark also carries the author, so the two never
    /// drift apart across tables.
    /// </summary>
    [Fact]
    public void EveryTableThatRecordsModifications_WhenBuildingTheModel_AlsoRecordsWho()
    {
        using DrinkItDbContext context = BuildModelOnlyContext();

        string[] incomplete =
        [
            .. context.Model.GetEntityTypes()
                .Where(entity => entity.FindProperty("LastModifiedAt") is not null)
                .Where(entity => entity.FindProperty("LastModifiedBy") is null
                    || entity.FindProperty("CreatedBy") is null)
                .Select(entity => entity.ClrType.Name)
                .Order(StringComparer.Ordinal)
        ];

        Assert.True(
            incomplete.Length == 0,
            $"These tables record when they were modified but not by whom: {string.Join(", ", incomplete)}");
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
