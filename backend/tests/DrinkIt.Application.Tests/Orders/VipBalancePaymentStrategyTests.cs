using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Tests.Tables;
using DrinkIt.Application.Tests.VipAccounts;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.Tests.Orders;

/// <summary>US-41: the third way of paying, the one the design always had in mind (§8.1).</summary>
public class VipBalancePaymentStrategyTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));

    private static readonly DateTimeOffset Tonight = Opening.AddHours(2);

    private static Night ANight() => Night.Create(
        TheVenue,
        "Saturday",
        Opening,
        Opening.AddHours(7),
        [
            StaffUser.Create(TheVenue, "main-bar", "hash", StaffRole.Kds),
            StaffUser.Create(TheVenue, "till-1", "hash", StaffRole.Cashier),
            StaffUser.Create(TheVenue, "martin", "hash", StaffRole.Waiter),
        ]);

    private static readonly Table TableFive = Table.Create(TheVenue, 5, 8, null, Opening.AddDays(-1));

    /// <summary>Two gin tonics at 4500: 9000.</summary>
    private static Order AnOrderIn(Night night) =>
        Order.Place(TheVenue, night.Id, "María", OrderCode.Parse("K-4821"), [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4_500m, 2, null)]);

    [Fact]
    public async Task SettleAsync_WhenTheBalanceCoversIt_PaysTheOrderForTheTableAndSpendsIt()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, TableFive, 20_000m, Opening.AddHours(-3));
        Order order = AnOrderIn(night);

        Error? refusal = await StrategyOver(account).SettleAsync(order, new PaymentRequest(account.AccessCode), CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentMethod.VipBalance, order.Method);
        Assert.Equal((TableFive.Id, 5), (order.TableId, order.TableNumber));
        Assert.Equal(9_000m, order.BalanceAmount);
        Assert.Equal(11_000m, account.Balance);
    }

    // US-41, criterion 4: invented, regenerated or of another venue — one answer.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-code-anybody-was-given")]
    public async Task SettleAsync_WhenTheCodeOpensNoAccount_FailsNotFoundAndPaysNothing(string? code)
    {
        Night night = ANight();
        Order order = AnOrderIn(night);

        Error? refusal = await StrategyOver(VipAccount.Open(night, TableFive, 20_000m, Opening.AddHours(-3)))
            .SettleAsync(order, new PaymentRequest(code), CancellationToken.None);

        Assert.Equal(VipAccountErrors.NotFound, refusal);
        Assert.Equal(OrderStatus.Cart, order.Status);
    }

    // A code from last Saturday is a code that ran out with its night (US-40, criterion 3).
    [Fact]
    public async Task SettleAsync_WhenTheAccountIsOfAnotherNight_FailsNotFound()
    {
        VipAccount lastSaturdays = VipAccount.Open(ANight(), TableFive, 20_000m, Opening.AddHours(-3));
        Order order = AnOrderIn(ANight());

        Error? refusal = await StrategyOver(lastSaturdays).SettleAsync(order, new PaymentRequest(lastSaturdays.AccessCode), CancellationToken.None);

        Assert.Equal(VipAccountErrors.NotFound, refusal);
    }

    // US-42, criterion 2: the balance gives all it has and the phone pays the rest.
    [Fact]
    public async Task SettleAsync_WhenTheDifferenceIsPaid_EmptiesTheBalanceAndPaysTheOrderForTheTable()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, TableFive, 5_000m, Opening.AddHours(-3));
        Order order = AnOrderIn(night);

        Error? refusal = await StrategyOver(account).SettleAsync(order, new PaymentRequest(account.AccessCode, 4_000m), CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentMethod.VipBalance, order.Method);
        Assert.Equal((TableFive.Id, 5), (order.TableId, order.TableNumber));
        Assert.Equal(5_000m, order.BalanceAmount);
        Assert.Equal(0m, account.Balance);
    }

    // US-42, a balance at zero: the difference is the whole order, still the table's.
    [Fact]
    public async Task SettleAsync_WhenTheBalanceIsEmptyAndTheWholeOrderIsPaid_PaysItForTheTable()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, TableFive, 5_000m, Opening.AddHours(-3));
        account.Spend(5_000m);
        Order order = AnOrderIn(night);

        Error? refusal = await StrategyOver(account).SettleAsync(order, new PaymentRequest(account.AccessCode, 9_000m), CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(5, order.TableNumber);
        Assert.Equal(0m, order.BalanceAmount);
        Assert.Equal(5_000m, account.Spent);
    }

    // US-42, the edge: another phone of the table spent between seeing the
    // difference and paying it. What the customer agreed to no longer adds up,
    // so nothing is charged and they see the new difference. Also when the
    // balance grew, and when they agreed to nothing at all (null).
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(3_000)]
    [InlineData(5_000)]
    public async Task SettleAsync_WhenTheAgreedDifferenceIsNotTheDifference_FailsBalanceChangedAndSpendsNothing(int? agreed)
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, TableFive, 5_000m, Opening.AddHours(-3));
        Order order = AnOrderIn(night);

        Error? refusal = await StrategyOver(account).SettleAsync(order, new PaymentRequest(account.AccessCode, agreed), CancellationToken.None);

        Assert.Equal(VipAccountErrors.BalanceChanged, refusal);
        Assert.Equal(OrderStatus.Cart, order.Status);
        Assert.Equal(5_000m, account.Balance);
    }

    // The balance grew (the admin raised the package) after the phone showed a
    // difference: paying it would charge the phone for what the balance covers.
    [Fact]
    public async Task SettleAsync_WhenADifferenceIsAgreedButTheBalanceCoversIt_FailsBalanceChanged()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, TableFive, 20_000m, Opening.AddHours(-3));
        Order order = AnOrderIn(night);

        Error? refusal = await StrategyOver(account).SettleAsync(order, new PaymentRequest(account.AccessCode, 1_000m), CancellationToken.None);

        Assert.Equal(VipAccountErrors.BalanceChanged, refusal);
        Assert.Equal(20_000m, account.Balance);
    }

    private static VipBalancePaymentStrategy StrategyOver(VipAccount account) =>
        new(new VipAccountsInMemory(account), new FakeTables(existing: TableFive), new FixedClock(Tonight));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
