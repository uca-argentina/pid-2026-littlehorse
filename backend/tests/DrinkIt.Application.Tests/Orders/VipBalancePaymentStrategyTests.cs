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

    // US-42 pays the difference; until then, the order is not placed and nothing is spent.
    [Fact]
    public async Task SettleAsync_WhenTheBalanceFallsShort_FailsBalanceShortAndSpendsNothing()
    {
        Night night = ANight();
        VipAccount account = VipAccount.Open(night, TableFive, 5_000m, Opening.AddHours(-3));
        Order order = AnOrderIn(night);

        Error? refusal = await StrategyOver(account).SettleAsync(order, new PaymentRequest(account.AccessCode), CancellationToken.None);

        Assert.Equal(VipAccountErrors.BalanceShort, refusal);
        Assert.Equal(OrderStatus.Cart, order.Status);
        Assert.Equal(5_000m, account.Balance);
    }

    private static VipBalancePaymentStrategy StrategyOver(VipAccount account) =>
        new(new VipAccountsInMemory(account), new FakeTables(existing: TableFive), new FixedClock(Tonight));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
