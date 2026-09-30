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

    /// <summary>
    /// How it is paid, or null while nobody has chosen. Stamped alongside
    /// <see cref="PaidAt"/>, or earlier, when the order waits for a gateway
    /// (<see cref="AwaitPayment"/>) — the KDS board reads it back to tell barra from
    /// mesa (US-15), so it has to outlive the moment the payment strategy
    /// spends settling the order.
    /// </summary>
    public PaymentMethod? Method { get; private set; }

    /// <summary>
    /// Where the customer pays it, while it waits for a gateway — Mercado
    /// Pago's page (US-24). Kept so a retry of the same confirmation sends them
    /// to the same page: one checkout per order, never two that could both be
    /// paid. Null for an order paid on the spot.
    /// </summary>
    public string? PaymentUrl { get; private set; }

    /// <summary>The same checkout's id, which the gateway's own button opens. Set with <see cref="PaymentUrl"/>.</summary>
    public string? PaymentCheckoutId { get; private set; }

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
    /// Confirmed, and waiting for the money (US-24): at the till in cash, or on
    /// Mercado Pago's page. Unpaid and out of the bar's sight. Only cash raises
    /// something, for the till's list; a digital payment is Mercado Pago's to
    /// collect, not the till's. The method is kept so everyone knows which it is.
    /// </summary>
    public void AwaitPayment(PaymentMethod method)
    {
        EnsureItIs(OrderStatus.Cart);

        Status = OrderStatus.AwaitingPayment;
        Method = method;
        if (method == PaymentMethod.Cash) _domainEvents.Add(new OrderAwaitingPayment(VenueId));
    }

    /// <summary>Where the gateway said it can be paid. Only while it waits for that payment.</summary>
    public void OfferCheckout(PaymentCheckout checkout)
    {
        EnsureItIs(OrderStatus.AwaitingPayment);

        PaymentCheckoutId = checkout.Id;
        PaymentUrl = checkout.Url;
    }

    /// <summary>
    /// Paid. Straight from the cart when the money is settled on the spot, or
    /// from <see cref="OrderStatus.AwaitingPayment"/> when a gateway approves it
    /// — <paramref name="at"/> is then the moment it was approved. Cash goes
    /// through <see cref="CollectCash"/> instead.
    /// </summary>
    public void Pay(DateTimeOffset at, PaymentMethod method)
    {
        // Cash only becomes money at the till, through CollectCash.
        if (Status == OrderStatus.AwaitingPayment && Method == PaymentMethod.Cash) throw InvalidTransition();

        EnsureItIs(OrderStatus.Cart, OrderStatus.AwaitingPayment);

        Status = OrderStatus.Paid;
        PaidAt = at;
        Method = method;
    }

    /// <summary>
    /// The till took the money (US-26): paid now, remembering who took it, and
    /// straight to the bar. The only way out of waiting for cash.
    /// </summary>
    public void CollectCash(DateTimeOffset at, string cashier)
    {
        if (string.IsNullOrWhiteSpace(cashier)) throw new DomainException(ErrorCodes.CollectorRequired, "Somebody has to have taken the money.");

        EnsureItIs(OrderStatus.AwaitingPayment);
        // Waiting for Mercado Pago, not for cash: its money is already on its way.
        if (Method != PaymentMethod.Cash) throw InvalidTransition();

        Status = OrderStatus.Paid;
        PaidAt = at;
        CollectedBy = cashier;
        Enqueue();
        _domainEvents.Add(new OrderCollected(VenueId));
    }

    /// <summary>
    /// The payment did not happen: rejected, abandoned on the gateway's page, or
    /// never made in time. No money came in, so there is nothing to give back,
    /// and it never reached the board, so the board hears nothing. Idempotent:
    /// the customer's return and the gateway's notification can both say so.
    /// </summary>
    /// <remarks>
    /// Not the bar's <c>Cancel(reason)</c>: nobody decided this, and there is
    /// no reason anybody typed.
    /// </remarks>
    public void CancelUnpaid()
    {
        if (Status == OrderStatus.Canceled) return;

        EnsureItIs(OrderStatus.AwaitingPayment);

        Status = OrderStatus.Canceled;
    }

    /// <summary>Handed to the bar. Nothing takes it from here until the KDS exists.</summary>
    public void Enqueue()
    {
        EnsureItIs(OrderStatus.Paid);

        Status = OrderStatus.Queued;
        _domainEvents.Add(new OrderQueued(VenueId));
    }

    /// <summary>
    /// The bar took it (US-16) — "Preparar" on the board, even while no paper
    /// comes out. Asking again for what already happened changes nothing: a
    /// double tap, or a retry over a bad signal, is not a second order taken.
    /// </summary>
    public void StartPreparing()
    {
        if (Status == OrderStatus.InPreparation) return;

        EnsureItIs(OrderStatus.Queued);

        Status = OrderStatus.InPreparation;
        _domainEvents.Add(new OrderPreparationStarted(VenueId));
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
        _domainEvents.Add(new OrderRequeued(VenueId));
    }

    /// <summary>
    /// The drink is made (US-18, criterion 1). Only from preparation: a queued
    /// order is prepared first. Asking again for what already happened changes
    /// nothing, so nothing is saved and the board's clock — the audit stamp of
    /// the last change — keeps counting from when it really became ready.
    /// </summary>
    public void MarkReady()
    {
        if (Status == OrderStatus.Ready) return;

        EnsureItIs(OrderStatus.InPreparation);

        Status = OrderStatus.Ready;
        _domainEvents.Add(new OrderReady(VenueId, Id));
    }

    /// <summary>Marked ready by mistake: back to the bar, as if it never was.</summary>
    public void ReturnToPreparation()
    {
        if (Status == OrderStatus.InPreparation) return;

        EnsureItIs(OrderStatus.Ready);

        Status = OrderStatus.InPreparation;
        _domainEvents.Add(new OrderReturnedToPreparation(VenueId));
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
        _domainEvents.Add(new OrderDelivered(VenueId));
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
        _domainEvents.Add(new OrderDeliveryUndone(VenueId));
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
        if (!expected.Contains(Status)) throw InvalidTransition();
    }

    private static DomainException InvalidTransition() =>
        new(ErrorCodes.InvalidTransition, "That cannot be done to this order any more.");
}
