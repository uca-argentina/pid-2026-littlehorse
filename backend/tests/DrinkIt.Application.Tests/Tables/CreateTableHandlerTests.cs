using DrinkIt.Application.Common;
using DrinkIt.Application.Tables;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tests.Tables;

public class CreateTableHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WhenTheNumberIsFree_AddsTheTableToTheVenueOfTheSignedInAdministrator()
    {
        FakeTables tables = new();

        Result<TableSummary> result = await HandlerOver(tables).HandleAsync(
            new CreateTableCommand(5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TheVenue, tables.Added!.VenueId);
        Assert.Equal(5, tables.Added.Number);
        Assert.Equal(Now, tables.Added.CreatedAt);
        Assert.Equal(tables.Added.Id, result.Value.Id);
        Assert.Equal(5, result.Value.Number);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNumberIsAlreadyUsedInThisVenue_Fails()
    {
        FakeTables tables = new(taken: 5);

        Result<TableSummary> result = await HandlerOver(tables).HandleAsync(
            new CreateTableCommand(5), CancellationToken.None);

        Assert.Equal(TableErrors.NumberTaken, result.Error);
        Assert.Null(tables.Added);
    }

    // A broken invariant is not an expected outcome: the domain throws, the API
    // turns it into a 400, and the handler does not restate the rules.
    [Fact]
    public async Task HandleAsync_WhenTheDomainRejectsTheNumber_ThrowsWithoutCreatingAnything()
    {
        FakeTables tables = new();

        DomainException error = await Assert.ThrowsAsync<DomainException>(
            () => HandlerOver(tables).HandleAsync(new CreateTableCommand(0), CancellationToken.None));

        Assert.Equal(Table.ErrorCodes.NumberOutOfRange, error.Code);
        Assert.Null(tables.Added);
    }

    private static CreateTableHandler HandlerOver(FakeTables tables) =>
        new(tables, new Fake.CurrentVenue(), new Fake.Clock());

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id => TheVenue;
        }

        public sealed class Clock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
