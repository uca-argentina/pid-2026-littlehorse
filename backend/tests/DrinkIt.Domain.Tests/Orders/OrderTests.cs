using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Domain.Tests.Orders;

public class OrderTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly Guid GinTonic = Guid.CreateVersion7();

    private static readonly Guid Fernet = Guid.CreateVersion7();

    private static NewOrderItem AGinTonic(int quantity = 1, string? note = null) =>
        new(GinTonic, "Gin Tonic", 4500m, quantity, note);

    private static NewOrderItem AFernet(int quantity = 1) =>
        new(Fernet, "Fernet con Coca", 4000m, quantity, null);

    private static Order AnOrder(params NewOrderItem[] items) =>
        Order.Place(AVenue, "María Quadro", OrderCode.Parse("K-4821"), items.Length == 0 ? [AGinTonic()] : items);

    [Fact]
    public void Place_WhenValid_StartsAsACartThatNobodyHasPaid()
    {
        Order order = AnOrder(AGinTonic(2), AFernet());

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(AVenue, order.VenueId);
        Assert.Equal("María Quadro", order.CustomerName);
        Assert.Equal("K-4821", order.Code.Value);
        Assert.Equal(OrderStatus.Cart, order.Status);
        Assert.Null(order.PaidAt);
        Assert.Equal(2, order.Items.Count);
    }

    // US-12: the order is born with the secret that lets whoever placed it
    // watch it, because there is no account to prove it belongs to them.
    [Fact]
    public void Place_Always_GivesTheOrderATokenOfItsOwn()
    {
        Order one = AnOrder();
        Order another = AnOrder();

        Assert.Equal(TrackingToken.Length, one.TrackingToken.Value.Length);
        Assert.NotEqual(one.TrackingToken.Value, another.TrackingToken.Value);
    }

    /// <summary>
    /// A finished order is one nobody is waiting on any more, so its link stops
    /// working: the one left in a browser's history on a shared phone stops
    /// being a way in the moment the drinks are handed over.
    /// </summary>
    [Fact]
    public void IsFinished_WhenTheOrderIsStillGoing_IsFalse()
    {
        Order order = AnOrder();

        Assert.False(order.IsFinished);

        order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);
        order.Enqueue();

        Assert.False(order.IsFinished);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Canceled)]
    public void IsFinished_WhenNobodyIsWaitingOnItAnyMore_IsTrue(OrderStatus status)
    {
        Assert.True(status.IsFinished());
    }

    [Theory]
    [InlineData(OrderStatus.Cart)]
    [InlineData(OrderStatus.AwaitingPayment)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Queued)]
    [InlineData(OrderStatus.InPreparation)]
    [InlineData(OrderStatus.Ready)]
    public void IsFinished_WhenItIsStillOnItsWay_IsFalse(OrderStatus status)
    {
        Assert.False(status.IsFinished());
    }

    // What the bar charges is the sum it can recompute from the lines: a total
    // stored apart from them is a total that can disagree with them.
    [Fact]
    public void Total_Always_AddsUpEveryLine()
    {
        Order order = AnOrder(AGinTonic(2), AFernet());

        Assert.Equal(13000m, order.Total);
    }

    [Fact]
    public void Place_WhenTheNoteIsWritten_KeepsItOnItsOwnLine()
    {
        Order order = AnOrder(AGinTonic(note: "sin hielo"), AFernet());

        Assert.Equal("sin hielo", order.Items.Single(item => item.ProductId == GinTonic).Note);
        Assert.Null(order.Items.Single(item => item.ProductId == Fernet).Note);
    }

    [Fact]
    public void Place_WhenVenueIdIsEmpty_ThrowsVenueRequired()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Order.Place(Guid.Empty, "María Quadro", OrderCode.First, [AGinTonic()]));

        Assert.Equal(Order.ErrorCodes.VenueRequired, error.Code);
    }

    // Criterion 3 of US-11, at the deepest level: an order nobody can be called
    // for is an order the bar cannot hand over.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Place_WhenThereIsNoName_ThrowsNameRequired(string name)
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Order.Place(AVenue, name, OrderCode.First, [AGinTonic()]));

        Assert.Equal(Order.ErrorCodes.NameRequired, error.Code);
    }

    [Fact]
    public void Place_WhenTheNameIsLongerThanATicketFits_ThrowsNameLength()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Order.Place(AVenue, new string('a', Order.CustomerNameMaxLength + 1), OrderCode.First, [AGinTonic()]));

        Assert.Equal(Order.ErrorCodes.NameLength, error.Code);
    }

    [Fact]
    public void Place_WhenThereAreNoItems_ThrowsEmpty()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Order.Place(AVenue, "María Quadro", OrderCode.First, []));

        Assert.Equal(Order.ErrorCodes.Empty, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Place_WhenALineAsksForNothing_ThrowsQuantityNotPositive(int quantity)
    {
        DomainException error = Assert.Throws<DomainException>(() => AnOrder(AGinTonic(quantity)));

        Assert.Equal(Order.ErrorCodes.QuantityNotPositive, error.Code);
    }

    // One line per product, with a quantity beside it. Two lines for the same
    // drink would let the same order disagree with itself about the note.
    [Fact]
    public void Place_WhenTheSameDrinkComesTwice_ThrowsDuplicateItem()
    {
        DomainException error = Assert.Throws<DomainException>(() => AnOrder(AGinTonic(), AGinTonic(2)));

        Assert.Equal(Order.ErrorCodes.DuplicateItem, error.Code);
    }

    [Fact]
    public void Place_WhenTheNoteIsTooLong_ThrowsNoteLength()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => AnOrder(AGinTonic(note: new string('x', OrderItem.NoteMaxLength + 1))));

        Assert.Equal(Order.ErrorCodes.NoteLength, error.Code);
    }

    // The price is the venue's, read from its own menu at this moment. Nothing
    // the phone sends decides what anything costs.
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Place_WhenAPriceIsNotPositive_ThrowsPriceNotPositive(decimal price)
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Order.Place(AVenue, "María Quadro", OrderCode.First, [new NewOrderItem(GinTonic, "Gin Tonic", price, 1, null)]));

        Assert.Equal(Order.ErrorCodes.PriceNotPositive, error.Code);
    }

    public class Paying
    {
        private static Order ACartOf(string name = "María Quadro") =>
            Order.Place(AVenue, name, OrderCode.Parse("K-4821"), [AGinTonic()]);

        [Fact]
        public void Pay_WhenItIsACart_MarksItPaidAndStampsTheMoment()
        {
            Order order = ACartOf();

            order.Pay(new DateTimeOffset(2026, 9, 17, 2, 30, 0, TimeSpan.Zero), PaymentMethod.Digital);

            Assert.Equal(OrderStatus.Paid, order.Status);
            Assert.Equal(new DateTimeOffset(2026, 9, 17, 2, 30, 0, TimeSpan.Zero), order.PaidAt);
        }

        // US-15: the KDS board tells barra from mesa by the method that paid
        // for the order, so the method has to survive past the moment of
        // payment instead of being spent choosing a strategy and forgotten.
        [Fact]
        public void Pay_WhenItIsACart_StoresThePaymentMethod()
        {
            Order order = ACartOf();

            order.Pay(DateTimeOffset.UtcNow, PaymentMethod.VipBalance);

            Assert.Equal(PaymentMethod.VipBalance, order.Method);
        }

        // Criterion 6 is answered before this, by the idempotency key; this is
        // the invariant underneath it, so a bug there can never charge twice.
        [Fact]
        public void Pay_WhenItWasAlreadyPaid_ThrowsInvalidTransition()
        {
            Order order = ACartOf();
            order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);

            DomainException error = Assert.Throws<DomainException>(() => order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital));

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }

        // Criterion 5: paid, and waiting at the bar. Nobody takes it from here
        // until the KDS exists, which is a later sprint.
        [Fact]
        public void Enqueue_WhenItIsPaid_PutsItInTheQueue()
        {
            Order order = ACartOf();
            order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);

            order.Enqueue();

            Assert.Equal(OrderStatus.Queued, order.Status);
        }

        [Fact]
        public void Enqueue_WhenItWasNotPaid_ThrowsInvalidTransition()
        {
            Order order = ACartOf();

            DomainException error = Assert.Throws<DomainException>(order.Enqueue);

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }

        // US-15: the KDS board learns about a new order through this, not by
        // polling — so a Queued that never raised it is a tablet that never
        // updates until somebody reloads by hand.
        [Fact]
        public void Enqueue_WhenItIsPaid_RaisesOrderQueued()
        {
            Order order = ACartOf();
            order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);

            order.Enqueue();

            OrderQueued raised = Assert.IsType<OrderQueued>(Assert.Single(order.DomainEvents));
            Assert.Equal(AVenue, raised.VenueId);
        }

        [Fact]
        public void Enqueue_WhenItIsAlreadyQueued_ThrowsInvalidTransition()
        {
            Order order = ACartOf();
            order.Pay(DateTimeOffset.UtcNow, PaymentMethod.Digital);
            order.Enqueue();

            DomainException error = Assert.Throws<DomainException>(order.Enqueue);

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }
    }

    public class Preparing
    {
        private static readonly DateTimeOffset PaidAt = new(2026, 9, 17, 2, 30, 0, TimeSpan.Zero);

        private static Order APaidOrder()
        {
            Order order = Order.Place(AVenue, "María Quadro", OrderCode.Parse("K-4821"), [AGinTonic()]);
            order.Pay(PaidAt, PaymentMethod.Digital);

            return order;
        }

        private static Order AQueuedOrder()
        {
            Order order = APaidOrder();
            order.Enqueue();
            order.ClearDomainEvents();

            return order;
        }

        private static Order AnOrderInPreparation()
        {
            Order order = AQueuedOrder();
            order.StartPreparing();
            order.ClearDomainEvents();

            return order;
        }

        // US-16, criterion 1: the bar took it, so it leaves Nuevos.
        [Fact]
        public void StartPreparing_WhenQueued_MovesItToInPreparation()
        {
            Order order = AQueuedOrder();

            order.StartPreparing();

            Assert.Equal(OrderStatus.InPreparation, order.Status);
        }

        [Fact]
        public void StartPreparing_WhenQueued_RaisesOrderPreparationStarted()
        {
            Order order = AQueuedOrder();

            order.StartPreparing();

            OrderPreparationStarted raised = Assert.IsType<OrderPreparationStarted>(Assert.Single(order.DomainEvents));
            Assert.Equal(AVenue, raised.VenueId);
        }

        // US-16, criterion 3: a double tap, or a retry over a bad signal, asks
        // for what already happened. It is already true, so nothing changes
        // and the board is not told about a change that did not happen.
        [Fact]
        public void StartPreparing_WhenAlreadyInPreparation_ChangesNothing()
        {
            Order order = AnOrderInPreparation();

            order.StartPreparing();

            Assert.Equal(OrderStatus.InPreparation, order.Status);
            Assert.Empty(order.DomainEvents);
        }

        [Fact]
        public void StartPreparing_WhenNotYetQueued_ThrowsInvalidTransition()
        {
            Order order = APaidOrder();

            DomainException error = Assert.Throws<DomainException>(order.StartPreparing);

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }

        // US-16, criterion 4: taken by mistake, back among the new ones.
        [Fact]
        public void ReturnToQueue_WhenInPreparation_PutsItBackInTheQueue()
        {
            Order order = AnOrderInPreparation();

            order.ReturnToQueue();

            Assert.Equal(OrderStatus.Queued, order.Status);
        }

        // The age on the board is counted from here, so keeping it is what
        // "conservando su antigüedad original" means.
        [Fact]
        public void ReturnToQueue_WhenInPreparation_KeepsWhenItWasPaid()
        {
            Order order = AnOrderInPreparation();

            order.ReturnToQueue();

            Assert.Equal(PaidAt, order.PaidAt);
        }

        [Fact]
        public void ReturnToQueue_WhenInPreparation_RaisesOrderRequeued()
        {
            Order order = AnOrderInPreparation();

            order.ReturnToQueue();

            OrderRequeued raised = Assert.IsType<OrderRequeued>(Assert.Single(order.DomainEvents));
            Assert.Equal(AVenue, raised.VenueId);
        }

        [Fact]
        public void ReturnToQueue_WhenAlreadyQueued_ChangesNothing()
        {
            Order order = AQueuedOrder();

            order.ReturnToQueue();

            Assert.Equal(OrderStatus.Queued, order.Status);
            Assert.Empty(order.DomainEvents);
        }

        [Fact]
        public void ReturnToQueue_WhenNeverQueued_ThrowsInvalidTransition()
        {
            Order order = APaidOrder();

            DomainException error = Assert.Throws<DomainException>(order.ReturnToQueue);

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }
    }

    public class ReadyAndDelivered
    {
        private static readonly DateTimeOffset PaidAt = new(2026, 9, 28, 1, 0, 0, TimeSpan.Zero);

        private static readonly DateTimeOffset DeliveredAt = PaidAt.AddMinutes(9);

        private static Order AnOrderInPreparation()
        {
            Order order = Order.Place(AVenue, "María Quadro", OrderCode.Parse("K-4821"), [AGinTonic()]);
            order.Pay(PaidAt, PaymentMethod.Digital);
            order.Enqueue();
            order.StartPreparing();
            order.ClearDomainEvents();

            return order;
        }

        private static Order AReadyOrder()
        {
            Order order = AnOrderInPreparation();
            order.MarkReady();
            order.ClearDomainEvents();

            return order;
        }

        private static Order ADeliveredOrder()
        {
            Order order = AReadyOrder();
            order.Deliver(DeliveredAt);
            order.ClearDomainEvents();

            return order;
        }

        // US-18, criterion 1. When it happened is the audit stamp's to write,
        // on save: the domain only moves the order.
        [Fact]
        public void MarkReady_WhenInPreparation_MovesItToReady()
        {
            Order order = AnOrderInPreparation();

            order.MarkReady();

            Assert.Equal(OrderStatus.Ready, order.Status);
        }

        // The event US-21's push will hang off, so it names the order.
        [Fact]
        public void MarkReady_WhenInPreparation_RaisesOrderReadyForThatOrder()
        {
            Order order = AnOrderInPreparation();

            order.MarkReady();

            OrderReady raised = Assert.IsType<OrderReady>(Assert.Single(order.DomainEvents));
            Assert.Equal(AVenue, raised.VenueId);
            Assert.Equal(order.Id, raised.OrderId);
        }

        // A double tap: already true, so nothing changes — and with nothing to
        // save, the audit stamp, which is the board's clock, does not move.
        [Fact]
        public void MarkReady_WhenAlreadyReady_ChangesNothing()
        {
            Order order = AReadyOrder();

            order.MarkReady();

            Assert.Equal(OrderStatus.Ready, order.Status);
            Assert.Empty(order.DomainEvents);
        }

        // "Sólo desde En preparación": a queued order has to be prepared first.
        [Fact]
        public void MarkReady_WhenStillQueued_ThrowsInvalidTransition()
        {
            Order order = Order.Place(AVenue, "María Quadro", OrderCode.Parse("K-4821"), [AGinTonic()]);
            order.Pay(PaidAt, PaymentMethod.Digital);
            order.Enqueue();

            DomainException error = Assert.Throws<DomainException>(order.MarkReady);

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }

        // Marked ready by mistake: back to the bar.
        [Fact]
        public void ReturnToPreparation_WhenReady_PutsItBackInPreparation()
        {
            Order order = AReadyOrder();

            order.ReturnToPreparation();

            Assert.Equal(OrderStatus.InPreparation, order.Status);
            Assert.IsType<OrderReturnedToPreparation>(Assert.Single(order.DomainEvents));
        }

        [Fact]
        public void ReturnToPreparation_WhenAlreadyInPreparation_ChangesNothing()
        {
            Order order = AnOrderInPreparation();

            order.ReturnToPreparation();

            Assert.Equal(OrderStatus.InPreparation, order.Status);
            Assert.Empty(order.DomainEvents);
        }

        // Delivered by hand, when the scan cannot be done.
        [Fact]
        public void Deliver_WhenReady_MarksItDeliveredAndStampsTheMoment()
        {
            Order order = AReadyOrder();

            order.Deliver(DeliveredAt);

            Assert.Equal(OrderStatus.Delivered, order.Status);
            Assert.Equal(DeliveredAt, order.DeliveredAt);
            Assert.IsType<OrderDelivered>(Assert.Single(order.DomainEvents));
        }

        [Fact]
        public void Deliver_WhenAlreadyDelivered_ChangesNothing()
        {
            Order order = ADeliveredOrder();

            order.Deliver(DeliveredAt.AddMinutes(1));

            Assert.Equal(DeliveredAt, order.DeliveredAt);
            Assert.Empty(order.DomainEvents);
        }

        [Fact]
        public void Deliver_WhenNotReadyYet_ThrowsInvalidTransition()
        {
            Order order = AnOrderInPreparation();

            DomainException error = Assert.Throws<DomainException>(() => order.Deliver(DeliveredAt));

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }

        // "Deshacer": a mistaken tap is caught right away, so the order is Ready again.
        [Fact]
        public void UndoDelivery_WithinTheWindow_PutsItBackToReady()
        {
            Order order = ADeliveredOrder();

            order.UndoDelivery(DeliveredAt + Order.DeliveryUndoWindow);

            Assert.Equal(OrderStatus.Ready, order.Status);
            Assert.Null(order.DeliveredAt);
            Assert.IsType<OrderDeliveryUndone>(Assert.Single(order.DomainEvents));
        }

        // Delivered is final. The undo is a short grace, not a way to reopen
        // any order handed over during the night.
        [Fact]
        public void UndoDelivery_AfterTheWindow_ThrowsUndoWindowPassed()
        {
            Order order = ADeliveredOrder();

            DomainException error = Assert.Throws<DomainException>(
                () => order.UndoDelivery(DeliveredAt + Order.DeliveryUndoWindow + TimeSpan.FromSeconds(1)));

            Assert.Equal(Order.ErrorCodes.UndoWindowPassed, error.Code);
            Assert.Equal(OrderStatus.Delivered, order.Status);
        }

        [Fact]
        public void UndoDelivery_WhenNotDelivered_ThrowsInvalidTransition()
        {
            Order order = AReadyOrder();

            DomainException error = Assert.Throws<DomainException>(() => order.UndoDelivery(DeliveredAt));

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }
    }
}
