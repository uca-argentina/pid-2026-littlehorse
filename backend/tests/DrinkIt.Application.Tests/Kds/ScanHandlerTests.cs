using DrinkIt.Application.Common;
using DrinkIt.Application.Kds;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Kds;

/// <summary>
/// US-20, with what was left of US-19: the bar scans the QR on the customer's
/// phone. A ready order is handed over; anything else is said out loud and left
/// as it was.
/// </summary>
public class ScanHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 29, 2, 0, 0, TimeSpan.Zero);

    private static Order AQueuedOrder()
    {
        Order order = Order.Place(
            TheVenue,
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse("K-4821"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.Pay(Now.AddMinutes(-10), PaymentMethod.Digital);
        order.Enqueue();

        return order;
    }

    private static Order AnOrderInPreparation()
    {
        Order order = AQueuedOrder();
        order.StartPreparing();

        return order;
    }

    private static Order AReadyOrder()
    {
        Order order = AnOrderInPreparation();
        order.MarkReady();

        return order;
    }

    private static ScanHandler HandlerOver(KdsOrdersInMemory orders) => new(orders, new FixedClock(Now));

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsReady_DeliversItAndSavesIt()
    {
        Order order = AReadyOrder();
        KdsOrdersInMemory orders = new(order);

        Result<ScannedOrder> result = await HandlerOver(orders)
            .HandleAsync(order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(new ScannedOrder("K-4821", "María Quadro", ScanOutcome.Delivered), result.Value);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(Now, order.DeliveredAt);
        Assert.Equal(1, orders.Saves);
    }

    /// <summary>
    /// US-19, criterion 3. The scan only hands over: with no printed ticket,
    /// the only QR there is is the customer's, and showing it early must not
    /// mark a drink ready that nobody made.
    /// </summary>
    // Another tablet handed it over in the same instant: this scan did nothing.
    [Fact]
    public async Task HandleAsync_WhenAnotherTabletHandedItOverAtTheSameTime_SaysItChangedMeanwhile()
    {
        Order order = AReadyOrder();
        KdsOrdersInMemory orders = new(order);
        orders.RefusesTheNextSave();

        Result<ScannedOrder> result = await HandlerOver(orders).HandleAsync(order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(OrderErrors.ChangedMeanwhile, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsInPreparation_SaysItIsNotReadyAndLeavesIt()
    {
        Order order = AnOrderInPreparation();
        KdsOrdersInMemory orders = new(order);

        Result<ScannedOrder> result = await HandlerOver(orders)
            .HandleAsync(order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(ScanOutcome.NotReadyYet, result.Value.Outcome);
        Assert.Equal(OrderStatus.InPreparation, order.Status);
        Assert.Equal(0, orders.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenTheOrderIsStillQueued_SaysItIsNotReadyAndLeavesIt()
    {
        Order order = AQueuedOrder();
        KdsOrdersInMemory orders = new(order);

        Result<ScannedOrder> result = await HandlerOver(orders)
            .HandleAsync(order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(ScanOutcome.NotReadyYet, result.Value.Outcome);
        Assert.Equal(OrderStatus.Queued, order.Status);
        Assert.Equal(0, orders.Saves);
    }

    /// <summary>
    /// US-19, criterion 4: two people claiming the same order. Unlike the
    /// "Entregado" button, which shrugs off a double tap, the scan says so.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheOrderWasAlreadyDelivered_SaysSoAndLeavesIt()
    {
        Order order = AReadyOrder();
        order.Deliver(Now.AddMinutes(-3));
        KdsOrdersInMemory orders = new(order);

        Result<ScannedOrder> result = await HandlerOver(orders)
            .HandleAsync(order.TrackingToken.Value, CancellationToken.None);

        Assert.Equal(ScanOutcome.AlreadyDelivered, result.Value.Outcome);
        Assert.Equal(Now.AddMinutes(-3), order.DeliveredAt);
        Assert.Equal(0, orders.Saves);
    }

    /// <summary>
    /// Another venue's order reads the same as no order at all: the double,
    /// like the real repository behind the global query filter, only holds
    /// this venue's.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenNoOrderHasThatToken_ReturnsUnknownCode()
    {
        Result<ScannedOrder> result = await HandlerOver(new KdsOrdersInMemory(AReadyOrder()))
            .HandleAsync(TrackingToken.New().Value, CancellationToken.None);

        Assert.Equal(KdsErrors.UnknownCode, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K-4821")]
    [InlineData("https://drink.it/boliche/orders/K-4821/9f3c2ba7d81e4c06a1b2c3d4e5f60718")]
    public async Task HandleAsync_WhenWhatWasReadIsNotAToken_ReturnsUnknownCode(string? read)
    {
        Result<ScannedOrder> result = await HandlerOver(new KdsOrdersInMemory(AReadyOrder()))
            .HandleAsync(read, CancellationToken.None);

        Assert.Equal(KdsErrors.UnknownCode, result.Error);
    }

    /// <summary>A reader that types into a field ends with Enter, and some add a space.</summary>
    [Fact]
    public async Task HandleAsync_WhenTheReaderAddsWhitespace_StillFindsTheOrder()
    {
        Order order = AReadyOrder();

        Result<ScannedOrder> result = await HandlerOver(new KdsOrdersInMemory(order))
            .HandleAsync($" {order.TrackingToken.Value}\r\n", CancellationToken.None);

        Assert.Equal(ScanOutcome.Delivered, result.Value.Outcome);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
