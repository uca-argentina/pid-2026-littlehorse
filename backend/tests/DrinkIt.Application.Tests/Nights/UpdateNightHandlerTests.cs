using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Tests.Staff;
using DrinkIt.Application.Tests.Tables;
using DrinkIt.Application.Tests.VipAccounts;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.Tests.Nights;

public class UpdateNightHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    private static readonly StaffUser MainBar = StaffUser.Create(TheVenue, "main-bar", "hash", StaffRole.Kds);
    private static readonly StaffUser Till = StaffUser.Create(TheVenue, "till-1", "hash", StaffRole.Cashier);
    private static readonly StaffUser Martin = StaffUser.Create(TheVenue, "martin", "hash", StaffRole.Waiter);
    private static readonly StaffUser VipBar = StaffUser.Create(TheVenue, "vip-bar", "hash", StaffRole.Kds);

    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_SavesTheChangesAndReturnsThem()
    {
        Night saturday = ASaturday();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday, late", Opening, Closing.AddHours(1), [MainBar.Id, Till.Id, Martin.Id]),
            CancellationToken.None);

        Assert.Equal("Saturday, late", result.Value.Name);
        Assert.Equal(Closing.AddHours(1), saturday.EndsAt);
        Assert.Equal(1, nights.Saves);
    }

    // A night never overlaps itself: moving its own end an hour is not a clash.
    [Fact]
    public async Task HandleAsync_WhenOnlyItsOwnHoursAreInTheWay_Succeeds()
    {
        Night saturday = ASaturday();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening.AddHours(1), Closing.AddHours(1), [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WhenTheNewHoursOverlapAnotherNight_FailsWithoutSaving()
    {
        Night saturday = ASaturday();
        Night sunday = Night.Create(TheVenue, "Sunday", Closing.AddHours(12), Closing.AddHours(19), [MainBar, Till]);
        NightsInMemory nights = new(saturday, sunday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Closing.AddHours(13), [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.Equal(NightErrors.Overlaps, result.Error);
        Assert.Equal(0, nights.Saves);
    }

    // Unknown, or another venue's: the global query filter hides both alike.
    [Fact]
    public async Task HandleAsync_WhenTheVenueHasNoSuchNight_FailsWithNotFound()
    {
        Result<NightSummary> result = await HandlerOver(new NightsInMemory()).HandleAsync(
            new UpdateNightCommand(Guid.CreateVersion7(), "Saturday", Opening, Closing, [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.Equal(NightErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenACrewMemberIsNotInTheVenue_FailsWithoutSaving()
    {
        Night saturday = ASaturday();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Closing, [MainBar.Id, Till.Id, Guid.CreateVersion7()]),
            CancellationToken.None);

        Assert.Equal(NightErrors.CrewMemberNotFound, result.Error);
        Assert.Equal(0, nights.Saves);
    }

    // The clock is the handler's, not the request's: an administrator cannot
    // edit a finished night by sending a moment of their own.
    [Fact]
    public async Task HandleAsync_WhenTheNightIsUnderway_JudgesTheEditAgainstTheServerClock()
    {
        Night saturday = ASaturday();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights, now: Opening.AddHours(3)).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Opening.AddHours(1), [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.Equal(Opening.AddHours(3), result.Value.EndsAt);
    }

    // US-37: a KDS's stock that night is only sold, shown and given back
    // while it works the night. Taken out of the crew with some still loaded,
    // it would be stuck: it has to be given back to the storeroom first.
    [Fact]
    public async Task HandleAsync_WhenItTakesOutAKdsThatHoldsStock_FailsWithBarHoldsStockWithoutSaving()
    {
        Night saturday = Night.Create(TheVenue, "Saturday", Opening, Closing, [MainBar, VipBar, Till]);
        NightsInMemory nights = new(saturday);
        nights.BarsHoldingStock.Add(VipBar.Id);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Closing, [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.Equal(NightErrors.BarHoldsStock, result.Error);
        Assert.Equal(0, nights.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenTheKdsTakenOutHoldsNothing_SavesTheNewCrew()
    {
        Night saturday = Night.Create(TheVenue, "Saturday", Opening, Closing, [MainBar, VipBar, Till]);
        NightsInMemory nights = new(saturday);
        nights.BarsHoldingStock.Add(MainBar.Id);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Closing, [MainBar.Id, Till.Id]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([MainBar.Id, Till.Id], saturday.CrewIds);
    }

    private static readonly Table TableFive = Table.Create(TheVenue, 5, 8, null, Opening.AddDays(-1));
    private static readonly Table TableSeven = Table.Create(TheVenue, 7, 10, null, Opening.AddDays(-1));

    private static Night ASaturdayWithAWaiter() => Night.Create(TheVenue, "Saturday", Opening, Closing, [MainBar, Till, Martin]);

    private static UpdateNightCommand KeepingTheCrew(Night night, params NightTableRequest[] tables) =>
        new(night.Id, night.Name, night.StartsAt, night.EndsAt, [MainBar.Id, Till.Id, Martin.Id], tables);

    [Fact]
    public async Task HandleAsync_WhenATableIsAdded_OpensItForTheNight()
    {
        Night saturday = ASaturdayWithAWaiter();
        VipAccountsInMemory accounts = new();

        Result<NightSummary> result = await HandlerOver(new NightsInMemory(saturday), accounts: accounts).HandleAsync(
            KeepingTheCrew(saturday, new NightTableRequest(TableFive.Id, 150_000m)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        VipAccount opened = Assert.Single(accounts.Added);
        Assert.Equal((saturday.Id, TableFive.Id, 150_000m), (opened.NightId, opened.TableId, opened.Budget));
    }

    [Fact]
    public async Task HandleAsync_WhenATableIsLeftOut_TakesItOutOfTheNight()
    {
        Night saturday = ASaturdayWithAWaiter();
        VipAccount five = VipAccount.Open(saturday, TableFive, 150_000m, Opening.AddHours(-3));
        VipAccount seven = VipAccount.Open(saturday, TableSeven, 90_000m, Opening.AddHours(-3));
        VipAccountsInMemory accounts = new(five, seven);
        NightsInMemory nights = new(saturday);

        await HandlerOver(nights, accounts: accounts).HandleAsync(
            KeepingTheCrew(saturday, new NightTableRequest(TableSeven.Id, 90_000m)), CancellationToken.None);

        Assert.Equal([five], accounts.Removed);
        Assert.Empty(accounts.Added);
        Assert.Equal(1, nights.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenABudgetChanges_KeepsTheNewOneOnTheSameAccount()
    {
        Night saturday = ASaturdayWithAWaiter();
        VipAccount five = VipAccount.Open(saturday, TableFive, 150_000m, Opening.AddHours(-3));
        VipAccountsInMemory accounts = new(five);

        await HandlerOver(new NightsInMemory(saturday), accounts: accounts).HandleAsync(
            KeepingTheCrew(saturday, new NightTableRequest(TableFive.Id, 200_000m)), CancellationToken.None);

        Assert.Equal(200_000m, five.Budget);
        Assert.Empty(accounts.Added);
        Assert.Empty(accounts.Removed);
    }

    // The rule is on the crew the night ends up with: taking out the last
    // waiter of a night that sells tables is refused too.
    [Fact]
    public async Task HandleAsync_WhenTheLastWaiterLeavesANightWithTables_ThrowsWaiterRequiredWithoutSaving()
    {
        Night saturday = ASaturdayWithAWaiter();
        VipAccountsInMemory accounts = new(VipAccount.Open(saturday, TableFive, 150_000m, Opening.AddHours(-3)));
        NightsInMemory nights = new(saturday);

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => HandlerOver(nights, accounts: accounts).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Closing, [MainBar.Id, Till.Id], [new(TableFive.Id, 150_000m)]),
            CancellationToken.None));

        Assert.Equal(Night.ErrorCodes.WaiterRequired, error.Code);
        Assert.Equal(0, nights.Saves);
    }

    // A client that edits the night without knowing about tables sends none:
    // that leaves them alone, it does not take every table out.
    [Fact]
    public async Task HandleAsync_WhenTablesAreNotSent_LeavesTheNightsTablesAsTheyAre()
    {
        Night saturday = ASaturdayWithAWaiter();
        VipAccount five = VipAccount.Open(saturday, TableFive, 150_000m, Opening.AddHours(-3));
        VipAccountsInMemory accounts = new(five);

        Result<NightSummary> result = await HandlerOver(new NightsInMemory(saturday), accounts: accounts).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday, late", Opening, Closing.AddHours(1), [MainBar.Id, Till.Id, Martin.Id], Tables: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(accounts.Removed);
        Assert.Equal(150_000m, five.Budget);
    }

    // Even then, the tables it keeps still need a waiter.
    [Fact]
    public async Task HandleAsync_WhenTablesAreNotSentAndTheLastWaiterLeaves_ThrowsWaiterRequired()
    {
        Night saturday = ASaturdayWithAWaiter();
        VipAccountsInMemory accounts = new(VipAccount.Open(saturday, TableFive, 150_000m, Opening.AddHours(-3)));

        DomainException error = await Assert.ThrowsAsync<DomainException>(() => HandlerOver(new NightsInMemory(saturday), accounts: accounts).HandleAsync(
            new UpdateNightCommand(saturday.Id, "Saturday", Opening, Closing, [MainBar.Id, Till.Id], Tables: null),
            CancellationToken.None));

        Assert.Equal(Night.ErrorCodes.WaiterRequired, error.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenATableIsNotThisVenues_FailsTableNotFoundWithoutSaving()
    {
        Night saturday = ASaturdayWithAWaiter();
        NightsInMemory nights = new(saturday);

        Result<NightSummary> result = await HandlerOver(nights).HandleAsync(
            KeepingTheCrew(saturday, new NightTableRequest(Guid.CreateVersion7(), 1_000m)), CancellationToken.None);

        Assert.Equal(NightErrors.TableNotFound, result.Error);
        Assert.Equal(0, nights.Saves);
    }

    private static Night ASaturday() => Night.Create(TheVenue, "Saturday", Opening, Closing, [MainBar, Till]);

    private static UpdateNightHandler HandlerOver(NightsInMemory nights, DateTimeOffset? now = null, VipAccountsInMemory? accounts = null) =>
        new(
            nights,
            new StaffUsersInMemory(MainBar, Till, Martin, VipBar),
            new FakeTables(others: [TableFive, TableSeven]),
            accounts ?? new VipAccountsInMemory(),
            new FixedClock(now ?? Opening.AddHours(-2)));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
