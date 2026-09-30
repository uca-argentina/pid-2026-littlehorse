using DrinkIt.Application.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Payments;

/// <summary>
/// A payment method that finishes somewhere else: once the order is saved,
/// the customer is sent to pay it there.
/// </summary>
/// <remarks>
/// Apart from <c>IPaymentStrategy</c>, and not a method added to it: cash and
/// the VIP balance settle without sending anybody anywhere, and they are being
/// written by somebody else (2026-09-30). Only the methods that hand off
/// implement this, and the others never hear about it.
/// </remarks>
public interface IHandsOffPayment
{
    /// <summary>Where the customer goes to pay the order, now that it is saved.</summary>
    Task<Result<PaymentCheckout?>> HandOffAsync(Order order, string venueSlug, CancellationToken cancellationToken);
}
