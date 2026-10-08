using DrinkIt.Domain.Common;

namespace DrinkIt.Domain.Orders;

/// <summary>
/// An order moved, and whoever is watching it has to hear of it (US-22). Every
/// event an order raises is one: there is no move a customer holding the link
/// has no business seeing.
/// </summary>
/// <remarks>
/// Named by its tracking token and not by its id, because that token is all
/// the customer's screen can prove it holds — the same proof the tracking
/// link asks for. The event never leaves the process; what does is a group
/// name derived from it, never the token itself.
/// </remarks>
public interface IOrderChanged : IDomainEvent
{
    TrackingToken TrackingToken { get; }
}
