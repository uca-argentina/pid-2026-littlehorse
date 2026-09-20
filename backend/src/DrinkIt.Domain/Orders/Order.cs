using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Orders;

/// <summary>
/// What somebody asked the bar for: the drinks, who to call for them, what it
/// came to, and where it is in its night.
/// </summary>
/// <remarks>
/// The aggregate root of the whole flow. It is loaded and saved whole — the
/// lines are its own and nobody reaches them from outside — because the rules
/// worth protecting are about the order as a unit: what it costs is the sum of
/// its lines, and it cannot be paid twice.
///
/// Transitions live here and nowhere else (§5 of the functional design). An
/// illegal one throws instead of answering false: a caller that ignored the
/// answer would carry a half-moved order forward, and that is the bug this
/// design exists to make impossible.
/// </remarks>
public sealed class Order : IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "order.venue_required";
        public const string NameRequired = "order.name_required";
        public const string NameLength = "order.name_length";
        public const string Empty = "order.empty";
        public const string QuantityNotPositive = "order.quantity_not_positive";
        public const string PriceNotPositive = "order.price_not_positive";
        public const string DuplicateItem = "order.duplicate_item";
        public const string NoteLength = "order.note_length";
        public const string InvalidTransition = "order.invalid_transition";
    }

    /// <summary>Room for a full name on a ticket, and no more.</summary>
    public const int CustomerNameMaxLength = 60;

    private readonly List<OrderItem> _items = [];

    /// <summary>
    /// The lines are filled in afterwards rather than taken here: EF Core reads
    /// a row back through this constructor and cannot hand a navigation to a
    /// parameter, so a constructor that asked for them would make the aggregate
    /// unloadable.
    /// </summary>
    private Order(Guid id, Guid venueId, string customerName, OrderCode code, TrackingToken trackingToken)
    {
        Id = id;
        VenueId = venueId;
        CustomerName = customerName;
        Code = code;
        TrackingToken = trackingToken;
        Status = OrderStatus.Cart;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    /// <summary>Who the bar calls for it.</summary>
    public string CustomerName { get; }

    /// <summary>What the customer shouts at the bar to claim it.</summary>
    public OrderCode Code { get; }

    /// <summary>
    /// The secret that lets whoever placed it watch it. There is no account to
    /// prove the order is theirs, so holding this is the proof. Never printed,
    /// never said out loud, never logged.
    /// </summary>
    public TrackingToken TrackingToken { get; }

    public OrderStatus Status { get; private set; }

    /// <summary>When it was paid, or null while nobody has. Stamped once.</summary>
    public DateTimeOffset? PaidAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    /// <summary>Derived, never stored: a total kept apart from the lines is one that can disagree with them.</summary>
    public decimal Total => _items.Sum(item => item.Total);

    /// <summary>Nobody is waiting on it any more, so its link stops working.</summary>
    public bool IsFinished => Status.IsFinished();

    public static Order Place(
        Guid venueId,
        string customerName,
        OrderCode code,
        IReadOnlyCollection<NewOrderItem> items)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Orders must belong to a venue.");
        if (string.IsNullOrWhiteSpace(customerName)) throw new DomainException(ErrorCodes.NameRequired, "The order needs a name to be called for.");

        string cleanName = customerName.Trim();

        if (cleanName.Length > CustomerNameMaxLength) throw new DomainException(ErrorCodes.NameLength, $"The name cannot be longer than {CustomerNameMaxLength} characters.");
        if (items.Count == 0) throw new DomainException(ErrorCodes.Empty, "An order with nothing in it cannot be placed.");
        if (items.Select(item => item.ProductId).Distinct().Count() != items.Count) throw new DomainException(ErrorCodes.DuplicateItem, "Each drink goes on one line, with a quantity beside it.");

        Order order = new(Guid.CreateVersion7(), venueId, cleanName, code, TrackingToken.New());

        order._items.AddRange(items.Select(ToItem));

        return order;
    }

    /// <summary>
    /// Paid. In this sprint that happens the moment the customer confirms — the
    /// gateway is simulated, as the brief allows — so there is one step and not
    /// a wait for anybody's callback.
    /// </summary>
    public void Pay(DateTimeOffset at)
    {
        EnsureItIs(OrderStatus.Cart);

        Status = OrderStatus.Paid;
        PaidAt = at;
    }

    /// <summary>Handed to the bar. Nothing takes it from here until the KDS exists.</summary>
    public void Enqueue()
    {
        EnsureItIs(OrderStatus.Paid);

        Status = OrderStatus.Queued;
    }

    private static OrderItem ToItem(NewOrderItem item)
    {
        if (item.Quantity <= 0) throw new DomainException(ErrorCodes.QuantityNotPositive, "Every drink in the order needs a quantity of at least one.");
        if (item.UnitPrice <= 0) throw new DomainException(ErrorCodes.PriceNotPositive, "A drink cannot be ordered at a price of zero.");

        string? note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim();

        if (note?.Length > OrderItem.NoteMaxLength) throw new DomainException(ErrorCodes.NoteLength, $"A note cannot be longer than {OrderItem.NoteMaxLength} characters.");

        return new OrderItem(Guid.CreateVersion7(), item.ProductId, item.ProductName, item.UnitPrice, item.Quantity, note);
    }

    /// <summary>
    /// The message names no state on purpose: it reaches the customer (ADR-0009),
    /// and "expected Paid, was Queued" is our vocabulary, not theirs.
    /// </summary>
    private void EnsureItIs(OrderStatus expected)
    {
        if (Status != expected) throw new DomainException(ErrorCodes.InvalidTransition, "That cannot be done to this order any more.");
    }
}
