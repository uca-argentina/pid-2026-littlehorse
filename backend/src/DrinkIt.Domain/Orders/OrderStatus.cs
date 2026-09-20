namespace DrinkIt.Domain.Orders;

/// <summary>
/// Where an order is in its night, as §5 of the functional design defines it.
/// </summary>
/// <remarks>
/// All eight are declared because they are the contract the whole system reads,
/// and the numbers are stored: nobody may renumber or reorder them. Only the
/// transitions US-11 needs are implemented on <see cref="Order"/> — the rest
/// belong to the KDS and the cashier, and inventing their rules before anybody
/// has asked for them is how a state machine ends up with rules nobody tested.
/// </remarks>
public enum OrderStatus
{
    /// <summary>Being put together. On the phone, never here — the API only ever sees it in passing.</summary>
    Cart = 0,

    /// <summary>Waiting to be paid in cash at the till. Out of Sprint 1.</summary>
    AwaitingPayment = 1,

    Paid = 2,

    /// <summary>Paid and waiting at the bar for somebody to take it.</summary>
    Queued = 3,

    InPreparation = 4,

    Ready = 5,

    Delivered = 6,

    Canceled = 7,
}

/// <summary>Questions about a status that more than one place asks.</summary>
public static class OrderStatuses
{
    /// <summary>
    /// Nobody is waiting on it any more.
    /// </summary>
    /// <remarks>
    /// An extension and not a property of <see cref="Order"/>, because the
    /// query behind the tracking screen reads a status out of the database
    /// without ever loading the aggregate. One answer to "is it over" rather
    /// than two that can drift apart.
    /// </remarks>
    public static bool IsFinished(this OrderStatus status) =>
        status is OrderStatus.Delivered or OrderStatus.Canceled;
}
