using DrinkIt.Domain.Common;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Domain.Tests.VipAccounts;

public class VipAccountTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);
    private static readonly DateTimeOffset BeforeItStarts = Opening.AddDays(-1);

    private static Night ANight(Guid? venue = null)
    {
        Guid venueId = venue ?? AVenue;

        return Night.Create(
            venueId,
            "Saturday 10/10",
            Opening,
            Closing,
            [StaffUser.Create(venueId, "main-bar", "hash", StaffRole.Kds), StaffUser.Create(venueId, "till-1", "hash", StaffRole.Cashier)]);
    }

    private static Table ATable(Guid? venue = null) => Table.Create(venue ?? AVenue, 5, 8, null, BeforeItStarts);

    [Fact]
    public void Open_WhenValid_PutsTheTableInTheNightWithItsBudgetAndNothingSpent()
    {
        Night night = ANight();
        Table table = ATable();

        VipAccount account = VipAccount.Open(night, table, 150_000m, BeforeItStarts);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal(AVenue, account.VenueId);
        Assert.Equal(night.Id, account.NightId);
        Assert.Equal(table.Id, account.TableId);
        Assert.Equal(150_000m, account.Budget);
        Assert.Equal(0m, account.Spent);
        Assert.Equal(150_000m, account.Balance);
    }

    // The code is a secret like the tracking token: whoever has it spends the balance.
    [Fact]
    public void Open_WhenTwoTablesAreOpened_GivesEachAnAccessCodeOfItsOwn()
    {
        Night night = ANight();

        VipAccount first = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);
        VipAccount second = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);

        Assert.Equal(VipAccount.AccessCodeLength, first.AccessCode.Length);
        Assert.NotEqual(first.AccessCode, second.AccessCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Open_WhenTheBudgetIsNotPositive_ThrowsBudgetOutOfRange(int budget)
    {
        DomainException error = Assert.Throws<DomainException>(
            () => VipAccount.Open(ANight(), ATable(), budget, BeforeItStarts));

        Assert.Equal(VipAccount.ErrorCodes.BudgetOutOfRange, error.Code);
    }

    [Fact]
    public void Open_WhenTheBudgetIsAboveTheLimit_ThrowsBudgetOutOfRange()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => VipAccount.Open(ANight(), ATable(), VipAccount.BudgetMaxValue + 1, BeforeItStarts));

        Assert.Equal(VipAccount.ErrorCodes.BudgetOutOfRange, error.Code);
    }

    // US-39: a table that was put away no longer shows up to be given a night.
    [Fact]
    public void Open_WhenTheTableIsPutAway_ThrowsTableInactive()
    {
        Table table = ATable();
        table.Deactivate();

        DomainException error = Assert.Throws<DomainException>(
            () => VipAccount.Open(ANight(), table, 1_000m, BeforeItStarts));

        Assert.Equal(VipAccount.ErrorCodes.TableInactive, error.Code);
    }

    [Fact]
    public void Open_WhenTheTableIsFromAnotherVenue_ThrowsTableFromAnotherVenue()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => VipAccount.Open(ANight(), ATable(Guid.CreateVersion7()), 1_000m, BeforeItStarts));

        Assert.Equal(VipAccount.ErrorCodes.TableFromAnotherVenue, error.Code);
    }

    // A night that ended is a record: what its tables had is what they had.
    [Fact]
    public void Open_WhenTheNightIsOver_ThrowsNightOver()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => VipAccount.Open(ANight(), ATable(), 1_000m, Closing));

        Assert.Equal(VipAccount.ErrorCodes.NightOver, error.Code);
    }

    [Fact]
    public void ChangeBudget_WhenValid_KeepsTheNewBudget()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);

        account.ChangeBudget(night, 2_500m, Opening);

        Assert.Equal(2_500m, account.Budget);
        Assert.Equal(2_500m, account.Balance);
    }

    [Fact]
    public void ChangeBudget_WhenTheNightIsOver_ThrowsNightOverAndKeepsTheBudget()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);

        DomainException error = Assert.Throws<DomainException>(() => account.ChangeBudget(night, 2_500m, Closing));

        Assert.Equal(VipAccount.ErrorCodes.NightOver, error.Code);
        Assert.Equal(1_000m, account.Budget);
    }

    [Fact]
    public void ChangeBudget_WhenTheBudgetIsNotPositive_ThrowsBudgetOutOfRange()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);

        DomainException error = Assert.Throws<DomainException>(() => account.ChangeBudget(night, 0m, Opening));

        Assert.Equal(VipAccount.ErrorCodes.BudgetOutOfRange, error.Code);
    }

    [Fact]
    public void EnsureCanLeave_WhenNothingWasSpentAndTheNightIsOn_DoesNotThrow()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);

        account.EnsureCanLeave(night, Opening);
    }

    [Fact]
    public void EnsureCanLeave_WhenTheNightIsOver_ThrowsNightOver()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);

        DomainException error = Assert.Throws<DomainException>(() => account.EnsureCanLeave(night, Closing));

        Assert.Equal(VipAccount.ErrorCodes.NightOver, error.Code);
    }

    // US-40, criterion 4: a code that leaked stops working, and the money stays.
    [Fact]
    public void RegenerateAccessCode_WhenTheNightIsOn_ReplacesTheCodeAndKeepsTheBalance()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);
        string leaked = account.AccessCode;

        account.RegenerateAccessCode(night, Opening);

        Assert.NotEqual(leaked, account.AccessCode);
        Assert.Equal(VipAccount.AccessCodeLength, account.AccessCode.Length);
        Assert.Equal(1_000m, account.Balance);
    }

    [Fact]
    public void RegenerateAccessCode_WhenTheNightIsOver_ThrowsNightOverAndKeepsTheCode()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, ATable(), 1_000m, BeforeItStarts);
        string code = account.AccessCode;

        DomainException error = Assert.Throws<DomainException>(() => account.RegenerateAccessCode(night, Closing));

        Assert.Equal(VipAccount.ErrorCodes.NightOver, error.Code);
        Assert.Equal(code, account.AccessCode);
    }

    // US-41: an order from the table takes its total from the balance.
    [Fact]
    public void Spend_WhenTheBalanceCoversIt_TakesItFromTheBalance()
    {
        VipAccount account = VipAccount.Open(ANight(), ATable(), 1_000m, BeforeItStarts);

        account.Spend(300m);

        Assert.Equal(300m, account.Spent);
        Assert.Equal(700m, account.Balance);
    }

    [Fact]
    public void Spend_WhenItTakesTheWholeBalance_LeavesItAtZero()
    {
        VipAccount account = VipAccount.Open(ANight(), ATable(), 1_000m, BeforeItStarts);

        account.Spend(1_000m);

        Assert.Equal(0m, account.Balance);
    }

    // Never below zero: §8.1 and US-42 cover the difference with another
    // method, they do not leave the table owing.
    [Fact]
    public void Spend_WhenTheBalanceFallsShort_ThrowsBalanceShortAndSpendsNothing()
    {
        VipAccount account = VipAccount.Open(ANight(), ATable(), 1_000m, BeforeItStarts);

        DomainException error = Assert.Throws<DomainException>(() => account.Spend(1_000.01m));

        Assert.Equal(VipAccount.ErrorCodes.BalanceShort, error.Code);
        Assert.Equal(0m, account.Spent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Spend_WhenTheAmountIsNotPositive_ThrowsAmountNotPositive(int amount)
    {
        VipAccount account = VipAccount.Open(ANight(), ATable(), 1_000m, BeforeItStarts);

        DomainException error = Assert.Throws<DomainException>(() => account.Spend(amount));

        Assert.Equal(VipAccount.ErrorCodes.AmountNotPositive, error.Code);
    }
}
