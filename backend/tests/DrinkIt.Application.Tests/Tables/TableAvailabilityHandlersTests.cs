using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tests.Tables;

public class TableAvailabilityHandlersTests
{
    private static Table ATable() => Table.Create(Guid.CreateVersion7(), 5, new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Deactivate_WhenTheTableExists_PutsItAwayAndSaves()
    {
        Table table = ATable();
        FakeTables tables = new(existing: table);

        Result<TableSummary> result = await new DeactivateTableHandler(tables).HandleAsync(table.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(table.IsActive);
        Assert.False(result.Value.IsActive);
        Assert.Equal(5, result.Value.Number);
        Assert.Equal(1, tables.Saves);
    }

    [Fact]
    public async Task Deactivate_WhenTheVenueHasNoSuchTable_FailsNotFoundAndSavesNothing()
    {
        FakeTables tables = new();

        Result<TableSummary> result = await new DeactivateTableHandler(tables).HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(TableErrors.NotFound, result.Error);
        Assert.Equal(0, tables.Saves);
    }

    [Fact]
    public async Task Reactivate_WhenTheTableWasPutAway_BringsItBackAndSaves()
    {
        Table table = ATable();
        table.Deactivate();
        FakeTables tables = new(existing: table);

        Result<TableSummary> result = await new ReactivateTableHandler(tables).HandleAsync(table.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(table.IsActive);
        Assert.True(result.Value.IsActive);
        Assert.Equal(1, tables.Saves);
    }

    [Fact]
    public async Task Reactivate_WhenTheVenueHasNoSuchTable_FailsNotFoundAndSavesNothing()
    {
        FakeTables tables = new();

        Result<TableSummary> result = await new ReactivateTableHandler(tables).HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(TableErrors.NotFound, result.Error);
        Assert.Equal(0, tables.Saves);
    }
}
