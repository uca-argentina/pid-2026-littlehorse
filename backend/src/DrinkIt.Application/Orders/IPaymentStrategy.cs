using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// What one way of paying does to an order: whether the money is in.
/// </summary>
/// <remarks>
/// The three payment methods converge on the same flow after the money is
/// settled, and the difference between them is exactly this step: digital
/// pays on the spot, cash leaves the order waiting at the till until a cashier
/// takes the money (§6 of the functional design), VIP will debit the table.
/// Adding one is adding a class, never an if in the handler.
///
/// What follows a payment is not a method's to decide (US-36): every paid
/// order goes into the queue of a bar, chosen when the money is in. That step
/// is the one flow they converge on, so it is written once, in whoever turns
/// an order paid, and a new method cannot forget it.
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
    /// The money is settled on the spot: there is nothing left for anybody to
    /// do before the drinks go to a bar.
    /// </summary>
    public void Settle(Order order) => order.Pay(clock.GetUtcNow(), Method);
}

/// <summary>
/// Cash at the till (§6, US-24). Nothing is settled here: the order waits
/// with its code until a cashier takes the money (US-26), and only then does it
/// reach a bar — whichever has the least work at that moment (US-36).
/// </summary>
public sealed class CashPaymentStrategy : IPaymentStrategy
{
    public PaymentMethod Method => PaymentMethod.Cash;

    public void Settle(Order order) => order.AwaitPayment(Method);
}
