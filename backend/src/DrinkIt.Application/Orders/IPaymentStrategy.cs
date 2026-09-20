using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// What one way of paying does to an order.
/// </summary>
/// <remarks>
/// The three payment methods converge on the same flow after the money is
/// settled, and the difference between them is exactly this step: digital
/// leaves the order paid, cash will leave it waiting at the till, VIP will
/// debit the table. Adding one is adding a class, never an if in the handler.
/// </remarks>
public interface IPaymentStrategy
{
    PaymentMethod Method { get; }

    void Pay(Order order);
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

    public void Pay(Order order) => order.Pay(clock.GetUtcNow());
}
