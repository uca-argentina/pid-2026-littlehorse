using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Kds;

/// <summary>What a scan did, or why it did nothing.</summary>
public enum ScanOutcome
{
    /// <summary>It was ready, and now it is handed over.</summary>
    Delivered,

    /// <summary>Paid but not made yet: nothing to hand over (US-19, criterion 3).</summary>
    NotReadyYet,

    /// <summary>Handed over before: somebody is claiming it twice (US-19, criterion 4).</summary>
    AlreadyDelivered,
}

/// <summary>The order a scan found, so the screen can say whose it was.</summary>
public sealed record ScannedOrder(string Code, string CustomerName, ScanOutcome Outcome);

/// <summary>
/// US-20, closing US-19: the bar scans the QR on the customer's phone.
/// </summary>
/// <remarks>
/// The scan only hands over. The wireframe's "one scan, no mode" also marks a
/// ticket ready, but no ticket is printed yet, so the only QR in the room is
/// the customer's — and showing it early must not mark ready a drink nobody
/// made. When the printer arrives, that is the step this grows.
///
/// Unlike the "Entregado" button, which shrugs off a double tap, a scan that
/// changes nothing says why: it is how the bar finds out two people are
/// claiming the same order.
/// </remarks>
public sealed class ScanHandler(IOrderRepository orders, TimeProvider clock)
{
    public async Task<Result<ScannedOrder>> HandleAsync(string? read, CancellationToken cancellationToken)
    {
        // Nothing read is nothing found: a body without it is not worth a 400.
        if (!TrackingToken.TryParse(read?.Trim(), out TrackingToken? token)) return KdsErrors.UnknownCode;

        Order? order = await orders.GetForUpdateAsync(token!, cancellationToken);

        if (order is null) return KdsErrors.UnknownCode;

        ScanOutcome outcome = order.Status switch
        {
            OrderStatus.Ready => ScanOutcome.Delivered,
            OrderStatus.Delivered => ScanOutcome.AlreadyDelivered,
            _ => ScanOutcome.NotReadyYet,
        };

        if (outcome == ScanOutcome.Delivered)
        {
            order.Deliver(clock.GetUtcNow());

            // Handed over by another tablet in the same instant: this scan did nothing.
            Result<Order> saved = await orders.SaveAsync(order, cancellationToken);

            if (!saved.IsSuccess) return saved.Error!;
        }

        return new ScannedOrder(order.Code.ToString(), order.CustomerName, outcome);
    }
}
