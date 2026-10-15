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
public sealed class Order : AuditStamps, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "order.venue_required";
        public const string NightRequired = "order.night_required";
        public const string KdsRequired = "order.kds_required";
        public const string NameRequired = "order.name_required";
        public const string NameLength = "order.name_length";
        public const string Empty = "order.empty";
        public const string QuantityNotPositive = "order.quantity_not_positive";
        public const string PriceNotPositive = "order.price_not_positive";
        public const string DuplicateItem = "order.duplicate_item";
        public const string NoteLength = "order.note_length";
        public const string InvalidTransition = "order.invalid_transition";
        public const string CollectorRequired = "order.collector_required";
        public const string UndoWindowPassed = "order.undo_window_passed";
    }

    /// <summary>Room for a full name on a ticket, and no more.</summary>
    public const int CustomerNameMaxLength = 60;

    private readonly List<OrderItem> _items = [];

    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// The lines are filled in afterwards rather than taken here: EF Core reads
    /// a row back through this constructor and cannot hand a navigation to a
    /// parameter, so a constructor that asked for them would make the aggregate
    /// unloadable.
    /// </summary>
    private Order(Guid id, Guid venueId, Guid? nightId, string customerName, OrderCode code, TrackingToken trackingToken)
    {
        Id = id;
        VenueId = venueId;
        NightId = nightId;
        CustomerName = customerName;
        Code = code;
        TrackingToken = trackingToken;
        Status = OrderStatus.Cart;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    /// <summary>
    /// The night it was placed in (US-35). Null only on orders from before
    /// nights existed: a night invented for them would say when the migration
    /// ran, which is the lie US-30 refused to tell about creation dates.
    /// </summary>
    public Guid? NightId { get; }

    /// <summary>
    /// The bar it goes to (US-36): the KDS whose stock it is taken from (US-37),
    /// chosen when it is paid. Null while nobody has paid — a cash order
    /// waiting at the till holds no bar — and on orders from before bars kept
    /// a stock of their own, for the same reason as <see cref="NightId"/>.
    /// </summary>
    public Guid? KdsId { get; private set; }

    /// <summary>The VIP table it is carried to (US-41), or null when it is picked up at the bar.</summary>
    public Guid? TableId { get; private set; }

    /// <summary>
    /// The number of that table as it was when the order was placed: what the
    /// bar, the waiter and the customer read ("Mesa 5"), for good.
    /// </summary>
    public int? TableNumber { get; private set; }

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

    /// <summary>
    /// How it was paid, or null while nobody has. Stamped once, alongside
    /// <see cref="PaidAt"/> — the KDS board reads it back to tell barra from
    /// mesa (US-15), so it has to outlive the moment the payment strategy
    /// spends settling the order.
    /// </summary>
    public PaymentMethod? Method { get; private set; }

    /// <summary>
    /// When the bar took it (US-36, for the metrics of US-47), or null while
    /// nobody is preparing it. Handing it back forgets it.
    /// </summary>
    public DateTimeOffset? PreparationStartedAt { get; private set; }

    /// <summary>When the drink was made, or null while it is not. Undoing it forgets it.</summary>
    public DateTimeOffset? ReadyAt { get; private set; }

    /// <summary>When it was handed over, or null while nobody has picked it up.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>
    /// The cashier who took the money for it (US-26), or null when nobody did:
    /// it was paid from the phone, or not yet. The till lists its own shift by it.
    /// </summary>
    public string? CollectedBy { get; private set; }

    /// <summary>
    /// How long after a delivery it can still be undone. The screen offers it
    /// for a few seconds; this is the margin a slow request still fits in.
    /// Delivered is final after that.
    /// </summary>
    public static readonly TimeSpan DeliveryUndoWindow = TimeSpan.FromSeconds(30);

    public IReadOnlyList<OrderItem> Items => _items;

    /// <summary>
    /// Raised and not yet reacted to. Whoever saves this aggregate atomically
    /// dispatches these and clears them — the domain itself never calls
    /// anything outside its own boundary.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Derived, never stored: a total kept apart from the lines is one that can disagree with them.</summary>
    public decimal Total => _items.Sum(item => item.Total);

    /// <summary>Nobody is waiting on it any more, so its link stops working.</summary>
    public bool IsFinished => Status.IsFinished();

    public static Order Place(
        Guid venueId,
        Guid nightId,
        string customerName,
        OrderCode code,
        IReadOnlyCollection<NewOrderItem> items)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Orders must belong to a venue.");
        if (nightId == Guid.Empty) throw new DomainException(ErrorCodes.NightRequired, "Orders are placed during a night of the venue.");
        if (string.IsNullOrWhiteSpace(customerName)) throw new DomainException(ErrorCodes.NameRequired, "The order needs a name to be called for.");

        string cleanName = customerName.Trim();

        if (cleanName.Length > CustomerNameMaxLength) throw new DomainException(ErrorCodes.NameLength, $"The name cannot be longer than {CustomerNameMaxLength} characters.");
        if (items.Count == 0) throw new DomainException(ErrorCodes.Empty, "An order with nothing in it cannot be placed.");
        if (items.Select(item => item.ProductId).Distinct().Count() != items.Count) throw new DomainException(ErrorCodes.DuplicateItem, "Each drink goes on one line, with a quantity beside it.");

        Order order = new(Guid.CreateVersion7(), venueId, nightId, cleanName, code, TrackingToken.New());

        order._items.AddRange(items.Select(ToItem));

        return order;
    }

    /// <summary>
    /// US-41: ordered with the code of a VIP table, so it is carried to it
    /// instead of picked up at the bar. Decided before paying, in the cart.
    /// </summary>
    public void ServeAtTable(Guid tableId, int tableNumber)
    {
        EnsureItIs(OrderStatus.Cart);

        TableId = tableId;
        TableNumber = tableNumber;
    }

    /// <summary>
    /// Paid from the phone, straight from the cart — the gateway is simulated,
    /// as the brief allows. Cash goes through <see cref="CollectCash"/> instead.
    /// Either way, what follows is <see cref="Enqueue"/> into a bar.
    /// </summary>
    public void Pay(DateTimeOffset at, PaymentMethod method)
    {
        EnsureItIs(OrderStatus.Cart);

        Status = OrderStatus.Paid;
        PaidAt = at;
        Method = method;
    }

    /// <summary>
    /// Confirmed, and waiting at the till for the money (§6, US-24). Unpaid and
    /// out of the bar's sight; what is raised is for the till's list, since
    /// nothing is to be made yet. The method is kept so the till knows what it
    /// is collecting.
    /// </summary>
    public void AwaitPayment(PaymentMethod method)
    {
        EnsureItIs(OrderStatus.Cart);

        Status = OrderStatus.AwaitingPayment;
        Method = method;
        _domainEvents.Add(new OrderAwaitingPayment(VenueId, TrackingToken));
    }

    /// <summary>
    /// The till took the money (US-26): paid now, remembering who took it. The
    /// only way out of waiting for cash; into a bar's queue next, like every
    /// paid order, with the bar chosen now and not when it was confirmed (US-36).
    /// </summary>
    public void CollectCash(DateTimeOffset at, string cashier)
    {
        if (string.IsNullOrWhiteSpace(cashier)) throw new DomainException(ErrorCodes.CollectorRequired, "Somebody has to have taken the money.");

        EnsureItIs(OrderStatus.AwaitingPayment);

        Status = OrderStatus.Paid;
        PaidAt = at;
        CollectedBy = cashier;
        _domainEvents.Add(new OrderCollected(VenueId, TrackingToken));
    }

    /// <summary>
    /// Into the queue of one bar (US-36): the flow every way of paying
    /// converges on once the money is in. A queued order always has its bar.
    /// </summary>
    public void Enqueue(Guid kdsId)
    {
        EnsureItIs(OrderStatus.Paid);

        if (kdsId == Guid.Empty) throw new DomainException(ErrorCodes.KdsRequired, "A paid order goes to a bar, whose stock it is taken from.");

        Status = OrderStatus.Queued;
        KdsId = kdsId;
        _domainEvents.Add(new OrderQueued(VenueId, KdsId, TrackingToken));
    }

    /// <summary>
    /// The bar took it (US-16) — "Preparar" on the board, even while no paper
    /// comes out. Asking again for what already happened changes nothing: a
    /// double tap, or a retry over a bad signal, is not a second order taken.
    /// </summary>
    public void StartPreparing(DateTimeOffset at)
    {
        if (Status == OrderStatus.InPreparation) return;

        EnsureItIs(OrderStatus.Queued);

        Status = OrderStatus.InPreparation;
        PreparationStartedAt = at;
        _domainEvents.Add(new OrderPreparationStarted(VenueId, KdsId, TrackingToken));
    }

    /// <summary>
    /// Taken by mistake, handed back (US-16, criterion 4). <see cref="PaidAt"/>
    /// stays as it was, and the board counts its age from there, so it goes
    /// back exactly as old as it was. Idempotent for the same reason as
    /// <see cref="StartPreparing"/>.
    /// </summary>
    public void ReturnToQueue()
    {
        if (Status == OrderStatus.Queued) return;

        EnsureItIs(OrderStatus.InPreparation);

        Status = OrderStatus.Queued;
        PreparationStartedAt = null;
        _domainEvents.Add(new OrderRequeued(VenueId, KdsId, TrackingToken));
    }

    /// <summary>
    /// The drink is made (US-18, criterion 1). Only from preparation: a queued
    /// order is prepared first. Asking again for what already happened changes
    /// nothing, so nothing is saved and the board's clock — the audit stamp of
    /// the last change — keeps counting from when it really became ready.
    /// </summary>
    public void MarkReady(DateTimeOffset at)
    {
        if (Status == OrderStatus.Ready) return;

        EnsureItIs(OrderStatus.InPreparation);

        Status = OrderStatus.Ready;
        ReadyAt = at;
        _domainEvents.Add(new OrderReady(VenueId, KdsId, Id, TrackingToken));
    }

    /// <summary>Marked ready by mistake: back to the bar, as if it never was.</summary>
    public void ReturnToPreparation()
    {
        if (Status == OrderStatus.InPreparation) return;

        EnsureItIs(OrderStatus.Ready);

        Status = OrderStatus.InPreparation;
        ReadyAt = null;
        _domainEvents.Add(new OrderReturnedToPreparation(VenueId, KdsId, TrackingToken));
    }

    /// <summary>
    /// Handed over. By hand for now (US-18), when there is no scan to verify
    /// who is picking it up; the scan of US-19 ends here too.
    /// </summary>
    public void Deliver(DateTimeOffset at)
    {
        if (Status == OrderStatus.Delivered) return;

        EnsureItIs(OrderStatus.Ready);

        Status = OrderStatus.Delivered;
        DeliveredAt = at;
        _domainEvents.Add(new OrderDelivered(VenueId, KdsId, TrackingToken));
    }

    /// <summary>
    /// "Deshacer", right after a mistaken delivery. Only within
    /// <see cref="DeliveryUndoWindow"/>: past it, a delivered order stays
    /// delivered.
    /// </summary>
    public void UndoDelivery(DateTimeOffset now)
    {
        EnsureItIs(OrderStatus.Delivered);

        // No moment recorded — delivered before the column existed — reads as
        // too late: an undo that cannot be timed must not reopen anything.
        if (DeliveredAt is null || now - DeliveredAt > DeliveryUndoWindow) throw new DomainException(ErrorCodes.UndoWindowPassed, "It is too late to undo that delivery.");

        Status = OrderStatus.Ready;
        DeliveredAt = null;
        _domainEvents.Add(new OrderDeliveryUndone(VenueId, KdsId, TrackingToken));
    }

    /// <summary>
    /// Not going ahead (US-23): only while it waits to be paid at the till,
    /// asked by the cashier or by the customer. Once paid, by any method, it is
    /// the bar's, and paid orders are not given back (docs/modelo-de-datos.md).
    /// Asking again for what already happened changes nothing: a double tap,
    /// or the cashier and the customer at once, is not a second cancellation.
    /// </summary>
    public void Cancel()
    {
        if (Status == OrderStatus.Canceled) return;

        EnsureItIs(OrderStatus.AwaitingPayment);

        Status = OrderStatus.Canceled;
        _domainEvents.Add(new OrderCanceled(VenueId, TrackingToken));
    }

    /// <summary>
    /// Forgets what was raised, once whoever saved this aggregate has reacted
    /// to it. Never called from inside the domain itself.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

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
    private void EnsureItIs(params ReadOnlySpan<OrderStatus> expected)
    {
        if (!expected.Contains(Status)) throw new DomainException(ErrorCodes.InvalidTransition, "That cannot be done to this order any more.");
    }
}
