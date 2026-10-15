using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Tests.Staff;
using DrinkIt.Application.Tests.Tables;
using DrinkIt.Application.Tests.VipAccounts;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tests.Nights;

public class CreateNightHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    private static readonly StaffUser MainBar = StaffUser.Create(TheVenue, "main-bar", "hash", StaffRole.Kds);
    private static readonly StaffUser Till = StaffUser.Create(TheVenue, "till-1", "hash", StaffRole.Cashier);
    private static readonly StaffUser Martin = StaffUser.Create(TheVenue, "martin", "hash", StaffRole.Waiter);

    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_AddsTheNightToTheVenueOfTheSignedInAdministrator()
    {
        NightsInMemory nights = new();

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id, Martin.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TheVenue, nights.Added!.VenueId);
        Assert.Equal("Saturday 10/10", nights.Added.Name);
        Assert.Equal([MainBar.Id, Till.Id, Martin.Id], nights.Added.CrewIds);
    }

    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_ReturnsWhatTheListingShows()
    {
        NightsInMemory nights = new();

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id),
            CancellationToken.None);

        Assert.Equal(nights.Added!.Id, result.Value.Id);
        Assert.Equal("Saturday 10/10", result.Value.Name);
        Assert.Equal(Opening, result.Value.StartsAt);
        Assert.Equal(Closing, result.Value.EndsAt);
        Assert.Equal([MainBar.Id, Till.Id], result.Value.CrewIds);
    }

    // US-37: a new night is saved with the storeroom split among its KDS,
    // both or neither. Only the KDS of its crew, and the server's clock
    // decides what the storeroom has; how much each gets is the night's rule.
    [Fact]
    public async Task HandleAsync_WhenTheNightIsCreated_AddsItWithItsKdsAndTheServerClock()
    {
        NightsInMemory nights = new();

        await HandlerOver(nights).HandleAsync(ACommand(MainBar.Id, Till.Id, Martin.Id), CancellationToken.None);

        Assert.Equal([MainBar.Id], nights.AddedBars!.Select(bar => bar.Id));
        Assert.Equal(Now, nights.AddedAt);
    }

    // Criterion 1: two nights of the same venue cannot share a single minute,
    // or an order confirmed in it would not know which one it belongs to.
    [Fact]
    public async Task HandleAsync_WhenItOverlapsAnotherNightOfTheVenue_FailsWithoutCreatingAnything()
    {
        Night friday = Night.Create(TheVenue, "Friday 9/10", Opening.AddHours(-20), Opening.AddHours(1), [MainBar, Till]);
        NightsInMemory nights = new(friday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id),
            CancellationToken.None);

        Assert.Equal(NightErrors.Overlaps, result.Error);
        Assert.Null(nights.Added);
    }

    // An id that is not in this venue — never existed, or belongs to another
    // one — reads the same: the global query filter hides both alike.
    [Fact]
    public async Task HandleAsync_WhenACrewMemberIsNotInTheVenue_FailsWithoutCreatingAnything()
    {
        NightsInMemory nights = new();

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.Equal(NightErrors.CrewMemberNotFound, result.Error);
        Assert.Null(nights.Added);
    }

    private static readonly Table TableFive = Table.Create(TheVenue, 5, 8, null, Opening.AddDays(-1));
    private static readonly Table TableSeven = Table.Create(TheVenue, 7, 10, null, Opening.AddDays(-1));

    [Fact]
    public async Task HandleAsync_WhenTablesAreChosen_OpensEachOneWithItsBudget()
    {
        VipAccountsInMemory accounts = new();

        Result<NightSummary> result = await HandlerOver(new NightsInMemory(), accounts).HandleAsync(
            ACommand(MainBar.Id, Till.Id, Martin.Id) with { Tables = [new(TableFive.Id, 150_000m), new(TableSeven.Id, 90_000m)] },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([(TableFive.Id, 150_000m), (TableSeven.Id, 90_000m)], accounts.Added.Select(account => (account.TableId, account.Budget)));
        Assert.All(accounts.Added, account => Assert.Equal(result.Value.Id, account.NightId));
    }

    [Fact]
    public async Task HandleAsync_WhenNoTablesAreChosen_OpensNone()
    {
        VipAccountsInMemory accounts = new();

        await HandlerOver(new NightsInMemory(), accounts).HandleAsync(ACommand(MainBar.Id, Till.Id), CancellationToken.None);

        Assert.Empty(accounts.Added);
    }

    // §8.3: somebody has to carry the drinks to the tables.
    [Fact]
    public async Task HandleAsync_WhenTablesAreChosenWithoutAWaiter_ThrowsWaiterRequiredWithoutAddingAnything()
    {
        NightsInMemory nights = new();
        VipAccountsInMemory accounts = new();

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => HandlerOver(nights, accounts).HandleAsync(
            ACommand(MainBar.Id, Till.Id) with { Tables = [new(TableFive.Id, 150_000m)] },
            CancellationToken.None));

        Assert.Equal(Night.ErrorCodes.WaiterRequired, error.Code);
        Assert.Null(nights.Added);
        Assert.Empty(accounts.Added);
    }

    [Fact]
    public async Task HandleAsync_WhenATableIsNotThisVenues_FailsTableNotFound()
    {
        NightsInMemory nights = new();

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            ACommand(MainBar.Id, Till.Id, Martin.Id) with { Tables = [new(Guid.CreateVersion7(), 150_000m)] },
            CancellationToken.None);

        Assert.Equal(NightErrors.TableNotFound, result.Error);
        Assert.Null(nights.Added);
    }

    [Fact]
    public async Task HandleAsync_WhenATableIsChosenTwice_FailsTableRepeated()
    {
        Result<NightSummary> result = await HandlerOver(new NightsInMemory()).HandleAsync(
            ACommand(MainBar.Id, Till.Id, Martin.Id) with { Tables = [new(TableFive.Id, 1_000m), new(TableFive.Id, 2_000m)] },
            CancellationToken.None);

        Assert.Equal(NightErrors.TableRepeated, result.Error);
    }

    private static CreateNightCommand ACommand(params Guid[] crewIds) =>
        new("Saturday 10/10", Opening, Closing, crewIds);

    private static readonly DateTimeOffset Now = Opening.AddHours(-5);

    private static CreateNightHandler HandlerOver(NightsInMemory nights, VipAccountsInMemory? accounts = null) =>
        new(
            nights,
            new StaffUsersInMemory(MainBar, Till, Martin),
            new FakeTables(others: [TableFive, TableSeven]),
            accounts ?? new VipAccountsInMemory(),
            new TheCurrentVenue(),
            new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    private sealed class TheCurrentVenue : ICurrentVenue
    {
        public Guid Id => TheVenue;
    }
}
