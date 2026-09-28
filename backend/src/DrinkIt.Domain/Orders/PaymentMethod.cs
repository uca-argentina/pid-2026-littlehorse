namespace DrinkIt.Domain.Orders;

/// <summary>
/// How an order gets paid for. The three converge on the same flow afterwards:
/// what changes is how the money arrives, never how the order is put together.
/// </summary>
/// <remarks>
/// All three are declared because they are the vocabulary of the design, and
/// the numbers are stored. Only <see cref="Digital"/> has a strategy behind it
/// in Sprint 1 — the brief leaves the till and the VIP tables out — and asking
/// for either of the others is answered, not ignored.
/// </remarks>
public enum PaymentMethod
{
    /// <summary>Card or Mercado Pago, from the phone. Simulated in Sprint 1.</summary>
    Digital = 0,

    /// <summary>Cash at the till. The order waits with a code the cashier looks up.</summary>
    Cash = 1,

    /// <summary>Charged against the table's VIP account.</summary>
    VipBalance = 2,
}

/// <summary>Questions about a payment method that more than one place asks.</summary>
public static class PaymentMethods
{
    /// <summary>
    /// Whether this method means the order goes to a VIP table instead of
    /// being picked up at the bar (US-15). The only one that does is the VIP
    /// balance — cash and digital both leave the drinks waiting at the bar,
    /// whichever moment the money actually changes hands.
    /// </summary>
    public static bool IsForTable(this PaymentMethod method) =>
        method == PaymentMethod.VipBalance;
}
