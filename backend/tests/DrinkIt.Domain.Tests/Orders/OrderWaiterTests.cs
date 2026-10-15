using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Domain.Tests.Orders;

/// <summary>
/// US-43: a waiter takes a table's order that is ready, so two of them never
/// carry the same one. Nobody assigns it (§8.3): whoever taps first has it.
/// </summary>
public class OrderWaiterTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly Guid ANight = Guid.CreateVersion7();

    private static readonly Guid MainBar = Guid.CreateVersion7();

    private static readonly Guid ATable = Guid.CreateVersion7();

    private static readonly Guid Martin = Guid.CreateVersion7();

    private static readonly Guid Sofia = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);

    private static NewOrderItem AGinTonic() =>
        new(Guid.CreateVersion7(), "Gin Tonic", 4500, 2, null);

    private static Order APaidTableOrder()
    {
        Order order = Order.Place(AVenue, ANight, "María Quadro", OrderCode.Parse("K-4821"), [AGinTonic()]);
        order.ServeAtTable(ATable, 5);
        order.PayFromBalance(Now, order.Total);
        order.Enqueue(MainBar);

        return order;
    }

    private static Order AReadyTableOrder()
    {
        Order order = APaidTableOrder();
        order.StartPreparing(Now);
        order.MarkReady(Now);
        order.ClearDomainEvents();

        return order;
    }

    private static Order AReadyBarOrder()
    {
        Order order = Order.Place(AVenue, ANight, "María Quadro", OrderCode.Parse("K-4822"), [AGinTonic()]);
        order.Pay(Now, PaymentMethod.Digital);
        order.Enqueue(MainBar);
        order.StartPreparing(Now);
        order.MarkReady(Now);
        order.ClearDomainEvents();

        return order;
    }

    // Criterion 2: it goes to "Las que tomaste", and the others read the name.
    [Fact]
    public void TakeToTable_WhenTheTableOrderIsReadyAndFree_KeepsWhoTookItAndWhen()
    {
        Order order = AReadyTableOrder();

        order.TakeToTable(Martin, "Martín", Now.AddMinutes(1));

        Assert.Equal(Martin, order.WaiterId);
        Assert.Equal("Martín", order.WaiterName);
        Assert.Equal(Now.AddMinutes(1), order.TakenAt);
        Assert.Equal(OrderStatus.Ready, order.Status);
    }

    [Fact]
    public void TakeToTable_WhenTheTableOrderIsFree_RaisesTheChangeTheOtherWaitersSee()
    {
        Order order = AReadyTableOrder();

        order.TakeToTable(Martin, "Martín", Now);

        TableOrderChanged raised = Assert.IsType<TableOrderChanged>(Assert.Single(order.DomainEvents));
        Assert.Equal(AVenue, raised.VenueId);
        Assert.Equal(order.TrackingToken, raised.TrackingToken);
    }

    // A double tap, or the same phone retrying after a dropped answer, is not
    // a second take and must not look like one to the others.
    [Fact]
    public void TakeToTable_WhenTheSameWaiterAsksAgain_ChangesNothingAndRaisesNothing()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Martin, "Martín", Now.AddMinutes(1));
        order.ClearDomainEvents();

        order.TakeToTable(Martin, "Martín", Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(1), order.TakenAt);
        Assert.Empty(order.DomainEvents);
    }

    // Criterion 3, as far as the domain goes: the first one keeps it.
    [Fact]
    public void TakeToTable_WhenAnotherWaiterHasIt_ThrowsAlreadyTakenAndKeepsTheFirst()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Martin, "Martín", Now.AddMinutes(1));

        DomainException error = Assert.Throws<DomainException>(() => order.TakeToTable(Sofia, "Sofía", Now.AddMinutes(2)));

        Assert.Equal(Order.ErrorCodes.AlreadyTaken, error.Code);
        Assert.Equal(Martin, order.WaiterId);
        Assert.Equal("Martín", order.WaiterName);
    }

    // Still being made: nothing to carry yet.
    [Fact]
    public void TakeToTable_WhenTheOrderIsNotReady_ThrowsInvalidTransition()
    {
        Order order = APaidTableOrder();
        order.StartPreparing(Now);

        DomainException error = Assert.Throws<DomainException>(() => order.TakeToTable(Martin, "Martín", Now));

        Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        Assert.Null(order.WaiterId);
    }

    // Picked up at the bar: it is not a waiter's to carry.
    [Fact]
    public void TakeToTable_WhenTheOrderIsPickedUpAtTheBar_ThrowsNotAtATable()
    {
        Order order = AReadyBarOrder();

        DomainException error = Assert.Throws<DomainException>(() => order.TakeToTable(Martin, "Martín", Now));

        Assert.Equal(Order.ErrorCodes.NotAtATable, error.Code);
    }

    [Fact]
    public void TakeToTable_WhenThereIsNoWaiter_ThrowsWaiterRequired()
    {
        Order order = AReadyTableOrder();

        DomainException error = Assert.Throws<DomainException>(() => order.TakeToTable(Guid.Empty, "Martín", Now));

        Assert.Equal(Order.ErrorCodes.WaiterRequired, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TakeToTable_WhenTheWaiterHasNoName_ThrowsWaiterRequired(string name)
    {
        Order order = AReadyTableOrder();

        DomainException error = Assert.Throws<DomainException>(() => order.TakeToTable(Martin, name, Now));

        Assert.Equal(Order.ErrorCodes.WaiterRequired, error.Code);
    }

    // What the waiters see changes when the bar finishes a table's order, so
    // the screen of each of them has to hear about it.
    [Fact]
    public void MarkReady_WhenTheOrderIsAtATable_RaisesTheChangeTheWaitersSee()
    {
        Order order = APaidTableOrder();
        order.StartPreparing(Now);
        order.ClearDomainEvents();

        order.MarkReady(Now);

        Assert.Contains(order.DomainEvents, raised => raised is TableOrderChanged);
    }

    [Fact]
    public void MarkReady_WhenTheOrderIsPickedUpAtTheBar_RaisesNothingForTheWaiters()
    {
        Order order = Order.Place(AVenue, ANight, "María Quadro", OrderCode.Parse("K-4823"), [AGinTonic()]);
        order.Pay(Now, PaymentMethod.Digital);
        order.Enqueue(MainBar);
        order.StartPreparing(Now);
        order.ClearDomainEvents();

        order.MarkReady(Now);

        Assert.DoesNotContain(order.DomainEvents, raised => raised is TableOrderChanged);
    }

    // Marked ready by mistake: it goes back to the bar, and whoever had taken
    // it is not carrying anything any more.
    [Fact]
    public void ReturnToPreparation_WhenAWaiterHadTakenIt_ForgetsThemAndTellsTheOthers()
    {
        Order order = AReadyTableOrder();
        order.TakeToTable(Martin, "Martín", Now.AddMinutes(1));
        order.ClearDomainEvents();

        order.ReturnToPreparation();

        Assert.Null(order.WaiterId);
        Assert.Null(order.WaiterName);
        Assert.Null(order.TakenAt);
        Assert.Contains(order.DomainEvents, raised => raised is TableOrderChanged);
    }

    [Fact]
    public void Deliver_WhenTheOrderIsAtATable_RaisesTheChangeTheWaitersSee()
    {
        Order order = AReadyTableOrder();

        order.Deliver(Now.AddMinutes(3));

        Assert.Contains(order.DomainEvents, raised => raised is TableOrderChanged);
    }
}
