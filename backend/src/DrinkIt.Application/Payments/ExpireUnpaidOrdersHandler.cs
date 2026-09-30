using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Payments;

/// <summary>
/// US-24: an order nobody paid within <see cref="DigitalPaymentStrategy.PaymentWindow"/>
/// is canceled and its drinks go back on the menu. The gateway refuses a
/// payment after the same moment, so nothing can arrive for it later.
/// </summary>
/// <remarks>
/// One venue per run, like every use case: whoever runs it resolves the venue
/// first, and the global query filter keeps every other venue's orders out.
/// </remarks>
public sealed class ExpireUnpaidOrdersHandler(IOrderRepository orders, TimeProvider clock)
{
    /// <summary>How many it canceled.</summary>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset before = clock.GetUtcNow() - DigitalPaymentStrategy.PaymentWindow;

        IReadOnlyList<Order> waiting = await orders.GetAwaitingPaymentCreatedBeforeAsync(before, cancellationToken);

        // The window is Mercado Pago's. Cash waits at the till for as long as
        // the customer takes to walk there.
        Order[] expired = [.. waiting.Where(order => order.Method == PaymentMethod.Digital)];

        foreach (Order order in expired) await UnpaidOrderMoves.CancelAsync(order, orders, cancellationToken);

        return expired.Length;
    }
}
