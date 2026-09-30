using DrinkIt.Application.Common;

namespace DrinkIt.Application.Payments;

/// <summary>Failures of paying through the gateway.</summary>
public static class PaymentErrors
{
    /// <summary>The gateway could not open a checkout: down, slow, or refusing our credentials.</summary>
    public static readonly Error GatewayUnavailable =
        new("payment.gateway_unavailable", "The payment could not be started. Try again in a moment.");

    /// <summary>
    /// The gateway knows no such payment, or it is for an order this venue
    /// does not have. One answer for both: telling them apart would say which
    /// orders exist elsewhere.
    /// </summary>
    public static readonly Error UnknownPayment =
        new("payment.unknown", "That payment is not for an order of this venue.");

    /// <summary>
    /// Money came in for an order already canceled — paid right as its time ran
    /// out. It stays canceled; this is the record that it has to be refunded.
    /// </summary>
    public static readonly Error PaidAfterCancel =
        new("payment.paid_after_cancel", "A payment was approved for an order that was already canceled.");

    /// <summary>No order of this venue has that code and link.</summary>
    public static readonly Error OrderNotFound =
        new("payment.order_not_found", "That link does not lead to an order.");
}
