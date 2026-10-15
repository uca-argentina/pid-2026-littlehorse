using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tests.Tables;

public class UpdateTableHandlerTests
{
    private static Table ATable() => Table.Create(Guid.CreateVersion7(), 5, 8, "Upstairs", new DateTimeOffset(2026, 10, 14, 21, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task HandleAsync_WhenTheTableExists_ChangesItAndSaves()
    {
        Table table = ATable();
        FakeTables tables = new(existing: table);

        Result<TableSummary> result = await new UpdateTableHandler(tables).HandleAsync(
            new UpdateTableCommand(table.Id, 12, "Next to the DJ booth"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value.Capacity);
        Assert.Equal("Next to the DJ booth", result.Value.Location);
        Assert.Equal(5, result.Value.Number);
        Assert.Equal(1, tables.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenTheVenueHasNoSuchTable_FailsNotFoundAndSavesNothing()
    {
        FakeTables tables = new();

        Result<TableSummary> result = await new UpdateTableHandler(tables).HandleAsync(
            new UpdateTableCommand(Guid.CreateVersion7(), 12, null), CancellationToken.None);

        Assert.Equal(TableErrors.NotFound, result.Error);
        Assert.Equal(0, tables.Saves);
    }

    // A broken invariant is the domain's to report; the API turns it into a 400.
    [Fact]
    public async Task HandleAsync_WhenTheDomainRejectsTheCapacity_ThrowsWithoutSaving()
    {
        Table table = ATable();
        FakeTables tables = new(existing: table);

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => new UpdateTableHandler(tables).HandleAsync(
            new UpdateTableCommand(table.Id, 0, null), CancellationToken.None));

        Assert.Equal(Table.ErrorCodes.CapacityOutOfRange, error.Code);
        Assert.Equal(0, tables.Saves);
    }
}
