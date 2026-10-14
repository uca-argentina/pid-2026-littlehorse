using DrinkIt.Application.Cashier;
using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Orders;
using DrinkIt.Application.Tests.Kds;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Cashier;

/// <summary>
/// US-26: the cashier looks the order up by the code the customer shows and
/// confirms the money is in. From there it is an order like any other.
/// </summary>
public class CollectCashHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Tonight = new(2026, 9, 28, 1, 12, 0, TimeSpan.Zero);

    private static readonly Guid TheNight = Guid.CreateVersion7();

    private static readonly Guid GinTonic = Guid.CreateVersion7();

    private static readonly BarSupply MainBar = new(Guid.CreateVersion7(), 0, new Dictionary<Guid, int> { [GinTonic] = 10 });

    private static Order AnOrderWaitingForCash(string code = "K-4821", int gins = 1)
    {
        Order order = Order.Place(
            TheVenue,
            TheNight,
            "María Quadro",
            OrderCode.Parse(code),
            [new NewOrderItem(GinTonic, "Gin Tonic", 4500m, gins, null)]);
        order.AwaitPayment(PaymentMethod.Cash);

        return order;
    }

    private static CollectCashHandler AHandlerOver(KdsOrdersInMemory orders, IBarsForOrdering? bars = null) =>
        new(
            orders,
            bars ?? new Bars(MainBar),
            new TonightIs(TheNight),
            new FixedClock(Tonight),
            new TheCashierIs("laura.caja"));

    // Criterion 2: paid now, not when the customer confirmed on the phone —
    // the bar counts its age from here — and straight into the bar's queue.
    [Fact]
    public async Task HandleAsync_WhenTheOrderAwaitsPayment_PaysItAndQueuesIt()
    {
        Order order = AnOrderWaitingForCash();
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Queued, order.Status);
        Assert.Equal(Tonight, order.PaidAt);
        Assert.Equal("laura.caja", order.CollectedBy);
        Assert.Equal(1, orders.Collections);
    }

    // US-36: the bar is chosen now, when the money is in, and not when the
    // customer confirmed: the work each bar has changed in between.
    [Fact]
    public async Task HandleAsync_WhenTwoBarsHaveItAll_SendsItToTheOneWithLessWork()
    {
        Order order = AnOrderWaitingForCash();
        BarSupply busy = new(Guid.CreateVersion7(), 4, new Dictionary<Guid, int> { [GinTonic] = 10 });
        BarSupply quiet = new(Guid.CreateVersion7(), 1, new Dictionary<Guid, int> { [GinTonic] = 10 });
        Bars bars = new(busy, quiet);

        await AHandlerOver(new KdsOrdersInMemory(order), bars).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(quiet.KdsId, order.KdsId);
        Assert.Equal(TheNight, bars.AskedFor);
    }

    // US-36: cash takes no stock until it is collected, so what was there at
    // confirmation may be gone. The till hears it before taking the money,
    // and the order keeps waiting, so the customer can change it or cancel.
    [Fact]
    public async Task HandleAsync_WhenNoBarHasItAll_RefusesWithoutCollectingAndNamesTheDrink()
    {
        Order order = AnOrderWaitingForCash(gins: 11);
        KdsOrdersInMemory orders = new(order);

        Result<OrderStatus> result = await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(CashierErrors.SoldOut.Code, result.Error!.Code);
        Assert.Equal("Gin Tonic", result.Error.Subject);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(0, orders.Collections);
    }

    // The night the order was placed in is over: its bars and their stock are
    // done with, and a bar of that night may not be working tonight. The till
    // is told so, and nothing is charged.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_WhenTheOrdersNightIsOver_RefusesWithoutCollecting(bool anotherNightIsOn)
    {
        Order order = AnOrderWaitingForCash();
        KdsOrdersInMemory orders = new(order);
        TonightIs nights = new(anotherNightIsOn ? Guid.CreateVersion7() : null);

        Result<OrderStatus> result = await new CollectCashHandler(orders, new Bars(MainBar), nights, new FixedClock(Tonight), new TheCashierIs("laura.caja"))
            .HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(CashierErrors.NightOver, result.Error);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(0, orders.Collections);
    }

    // Somebody ordered the last gin from the phone between the read and the
    // write: the till does not hear about the race, only about running out.
    [Fact]
    public async Task HandleAsync_WhenTheStockMovedUnderneath_TriesAgainAndCollects()
    {
        KdsOrdersInMemory orders = new(AnOrderWaitingForCash());
        orders.LosesTheNextCollectionToASale(reloadedAs: AnOrderWaitingForCash());

        Result<OrderStatus> result = await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, orders.Collections);
    }

    // Criterion 3, and the double tap of the wireframe: collecting twice is
    // refused out loud, never charged twice and never an error page.
    [Fact]
    public async Task HandleAsync_WhenTheOrderWasAlreadyPaid_RefusesWithoutSaving()
    {
        Order order = AnOrderWaitingForCash();
        KdsOrdersInMemory orders = new(order);
        await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Result<OrderStatus> again = await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(CashierErrors.AlreadyPaid, again.Error);
        Assert.Equal(1, orders.Collections);
    }

    // Two tills collected it in the same instant and the other one got there
    // first: for this one it is already paid, and the screen says so.
    [Fact]
    public async Task HandleAsync_WhenAnotherTillCollectedItAtTheSameTime_RefusesAsAlreadyPaid()
    {
        KdsOrdersInMemory orders = new(AnOrderWaitingForCash());
        orders.RefusesTheNextSave();

        Result<OrderStatus> result = await AHandlerOver(orders).HandleAsync("K-4821", CancellationToken.None);

        Assert.Equal(CashierErrors.AlreadyPaid, result.Error);
    }

    // Another venue's order looks exactly like this too: the filter hides it.
    [Theory]
    [InlineData("K-9999")]
    [InlineData("not-a-code")]
    public async Task HandleAsync_WhenNoOrderHasThatCode_ReturnsNotFound(string code)
    {
        Result<OrderStatus> result = await AHandlerOver(new KdsOrdersInMemory(AnOrderWaitingForCash()))
            .HandleAsync(code, CancellationToken.None);

        Assert.Equal(CashierErrors.OrderNotFound, result.Error);
    }

    private sealed class TheCashierIs(string username) : ICurrentStaffUser
    {
        public string? Username => username;

        public Guid? Id => null;
    }

    private sealed class Bars(params BarSupply[] bars) : IBarsForOrdering
    {
        public Guid? AskedFor { get; private set; }

        public Task<IReadOnlyList<BarSupply>> GetAsync(Guid nightId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
        {
            AskedFor = nightId;

            return Task.FromResult<IReadOnlyList<BarSupply>>(bars);
        }
    }

    private sealed class TonightIs(Guid? night) : IUnderwayNightLookup
    {
        public Task<Guid?> FindIdAsync(DateTimeOffset at, CancellationToken cancellationToken) => Task.FromResult(night);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
