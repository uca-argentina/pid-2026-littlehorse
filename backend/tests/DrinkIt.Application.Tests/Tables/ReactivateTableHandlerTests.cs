using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tests.Tables;

public class ReactivateTableHandlerTests
{
    private static Table ATable() => Table.Create(Guid.CreateVersion7(), 5, 8, null, new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task HandleAsync_WhenTheTableWasPutAway_BringsItBackAndSaves()
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
    public async Task HandleAsync_WhenTheVenueHasNoSuchTable_FailsNotFoundAndSavesNothing()
    {
        FakeTables tables = new();

        Result<TableSummary> result = await new ReactivateTableHandler(tables).HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(TableErrors.NotFound, result.Error);
        Assert.Equal(0, tables.Saves);
    }
}
