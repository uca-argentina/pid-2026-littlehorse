using DrinkIt.Application.Common;
using DrinkIt.Application.Payments;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>
/// What one way of paying does to an order, and how far it takes it.
/// </summary>
/// <remarks>
/// The three payment methods converge on the same flow after the money is
/// settled, and the difference between them is exactly this step: digital
/// leaves the order paid and in the bar's queue, cash leaves it waiting at
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
/// Card or Mercado Pago, through Checkout Pro (US-24). Confirming no longer
/// pays: the order waits, and the customer is sent to Mercado Pago's page to
/// pay it. The answer — approved, rejected, or never — moves it on from there.
/// </summary>
// Still here and not in Infrastructure: it only talks to the gateway through
// its port, IPaymentGateway, which is what lives on the other side.
public sealed class DigitalPaymentStrategy(IPaymentGateway gateway, TimeProvider clock) : IPaymentStrategy, IHandsOffPayment
{
    /// <summary>
    /// How long a customer has to pay (decided on 2026-09-30): nobody waits
    /// longer for a drink they have not paid for, and the gateway refuses the
    /// payment after it.
    /// </summary>
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);

    public PaymentMethod Method => PaymentMethod.Digital;

    /// <summary>Nothing is paid yet, so nothing goes to the bar.</summary>
    public void Settle(Order order) => order.AwaitPayment(Method);

    public async Task<Result<PaymentCheckout?>> HandOffAsync(Order order, string venueSlug, CancellationToken cancellationToken)
    {
        Result<PaymentCheckout> checkout = await gateway.StartCheckoutAsync(
            new PaymentCheckoutRequest(order, venueSlug, clock.GetUtcNow() + PaymentWindow),
            cancellationToken);

        if (!checkout.IsSuccess) return checkout.Error!;

        order.OfferCheckout(checkout.Value);

        return checkout.Value;
    }
}

/// <summary>
/// Cash at the till (§6, US-24). Nothing is settled here: the order waits
/// with its code until a cashier takes the money (US-26), and only then does it
/// reach the bar.
/// </summary>
public sealed class CashPaymentStrategy : IPaymentStrategy
{
    public PaymentMethod Method => PaymentMethod.Cash;

    public void Settle(Order order) => order.AwaitPayment(Method);
}
