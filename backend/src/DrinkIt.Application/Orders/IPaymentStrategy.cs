using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// What one way of paying does to an order, and how far it takes it.
/// </summary>
/// <remarks>
/// The three payment methods converge on the same flow after the money is
/// settled, and the difference between them is exactly this step: digital
/// leaves the order paid and in the bar's queue, cash will leave it waiting at
/// the till until a cashier takes the money (§6 of the functional design), VIP
/// will debit the table. Adding one is adding a class, never an if in the
/// handler.
///
/// Which is why this settles rather than pays: where the order ends up is part
/// of what a payment method decides. A handler that paid and then queued on its
/// own would have to grow a branch the day cash arrives, and the whole point of
/// this port is that it does not.
/// </remarks>
public interface IPaymentStrategy
{
    PaymentMethod Method { get; }

    void Settle(Order order);
}

/// <summary>
/// Card or Mercado Pago. Simulated in Sprint 1, which the brief allows: there
/// is no gateway to call, so confirming settles the money there and then.
/// </summary>
// Lives here, not in Infrastructure, because it has no external dependency
// yet (just TimeProvider). Once a real gateway is wired in, move this class
// to DrinkIt.Infrastructure so only its IPaymentStrategy port stays here.
public sealed class DigitalPaymentStrategy(TimeProvider clock) : IPaymentStrategy
{
    public PaymentMethod Method => PaymentMethod.Digital;

    /// <summary>
    /// The money is settled on the spot, so the drinks go straight to the bar:
    /// there is nothing left for anybody to do before they are made.
    /// </summary>
    public void Settle(Order order)
    {
        order.Pay(clock.GetUtcNow());
        order.Enqueue();
    }
}
