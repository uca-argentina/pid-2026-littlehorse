namespace DrinkIt.Domain.Orders;

/// <summary>
/// Where an order waiting for a gateway is paid (US-24): the checkout's id —
/// what the gateway's own button opens — and its link, for a plain redirect.
/// One without the other is never useful, so they travel together.
/// </summary>
/// <remarks>
/// Written out longhand rather than as a positional record, like
/// <see cref="NewOrderItem"/>: those generate init setters, and
/// DrinkIt.ArchitectureTests refuses a public setter anywhere in the domain.
/// </remarks>
public sealed record PaymentCheckout
{
    public PaymentCheckout(string id, string url)
    {
        Id = id;
        Url = url;
    }

    public string Id { get; }

    public string Url { get; }
}
