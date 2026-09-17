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

        order.Pay(DateTimeOffset.UtcNow);
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

            order.Pay(new DateTimeOffset(2026, 9, 17, 2, 30, 0, TimeSpan.Zero));

            Assert.Equal(OrderStatus.Paid, order.Status);
            Assert.Equal(new DateTimeOffset(2026, 9, 17, 2, 30, 0, TimeSpan.Zero), order.PaidAt);
        }

        // Criterion 6 is answered before this, by the idempotency key; this is
        // the invariant underneath it, so a bug there can never charge twice.
        [Fact]
        public void Pay_WhenItWasAlreadyPaid_ThrowsInvalidTransition()
        {
            Order order = ACartOf();
            order.Pay(DateTimeOffset.UtcNow);

            DomainException error = Assert.Throws<DomainException>(() => order.Pay(DateTimeOffset.UtcNow));

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }

        // Criterion 5: paid, and waiting at the bar. Nobody takes it from here
        // until the KDS exists, which is a later sprint.
        [Fact]
        public void Enqueue_WhenItIsPaid_PutsItInTheQueue()
        {
            Order order = ACartOf();
            order.Pay(DateTimeOffset.UtcNow);

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

        [Fact]
        public void Enqueue_WhenItIsAlreadyQueued_ThrowsInvalidTransition()
        {
            Order order = ACartOf();
            order.Pay(DateTimeOffset.UtcNow);
            order.Enqueue();

            DomainException error = Assert.Throws<DomainException>(order.Enqueue);

            Assert.Equal(Order.ErrorCodes.InvalidTransition, error.Code);
        }
    }
}
