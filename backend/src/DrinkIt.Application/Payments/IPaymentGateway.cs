using DrinkIt.Application.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Payments;

/// <summary>
/// What the gateway needs to open a checkout for one order: its lines, and the
/// venue whose addresses the customer and the gateway's notifications come
/// back to.
/// </summary>
/// <param name="Order">What is being paid: its lines, total and id.</param>
/// <param name="VenueSlug">The venue in the addresses the customer and the notifications return to.</param>
/// <param name="ExpiresAt">After this the gateway refuses the payment.</param>
public sealed record PaymentCheckoutRequest(Order Order, string VenueSlug, DateTimeOffset ExpiresAt);

/// <summary>Where a payment stands, as far as the order cares.</summary>
public enum GatewayPaymentStatus
{
    /// <summary>The money came in.</summary>
    Approved,

    /// <summary>Not decided yet: under review, or cash to be paid somewhere else.</summary>
    Pending,

    /// <summary>It will not come in: rejected, or canceled on the gateway's side.</summary>
    Rejected,
}

/// <summary>A payment as the gateway reports it, read back from the gateway and never from a URL.</summary>
public sealed record GatewayPayment(string Id, Guid OrderId, GatewayPaymentStatus Status, DateTimeOffset? ApprovedAt);

/// <summary>
/// The payment gateway — Mercado Pago's Checkout Pro (US-24). Implemented in
/// Infrastructure; nothing here knows which gateway it is.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Opens a checkout for the order and answers where the customer pays it.</summary>
    Task<Result<PaymentCheckout>> StartCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>The payment with that id, or null when the gateway knows none.</summary>
    Task<GatewayPayment?> FindPaymentAsync(string paymentId, CancellationToken cancellationToken);
}
