using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Orders;

/// <summary>One drink the customer is asking for. What it costs is not their business to say.</summary>
public sealed record OrderLineRequest(Guid ProductId, int Quantity, string? Note);

public sealed record ConfirmOrderCommand(
    string? CustomerName,
    PaymentMethod Method,
    string? IdempotencyKey,
    IReadOnlyCollection<OrderLineRequest> Lines);

/// <summary>The order as the confirmation screen shows it.</summary>
public sealed record ConfirmedOrder(
    Guid Id,
    string Code,
    string CustomerName,
    decimal Total,
    OrderStatus Status,
    DateTimeOffset PaidAt);

/// <summary>
/// Turns what somebody put together on their phone into an order of this venue:
/// priced from its own menu, paid, and handed to the bar with a code.
/// </summary>
/// <remarks>
/// The venue comes from <see cref="ICurrentVenue"/>, which on this route was
/// resolved from the slug in the address — the QR on the wall — so a customer
/// cannot order from one venue against another's menu.
/// </remarks>
public sealed class ConfirmOrderHandler(
    IOrderRepository orders,
    IProductsForOrdering products,
    IOrderCodeSequence codes,
    IEnumerable<IPaymentStrategy> paymentStrategies,
    ICurrentVenue currentVenue)
{
    public static readonly Error Empty =
        new("order.empty", "There is nothing in the order.");

    public static readonly Error IdempotencyKeyRequired =
        new("order.idempotency_key_required", "The order needs a key that tells one attempt from another.");

    public static readonly Error PaymentMethodUnavailable =
        new("order.payment_method_unavailable", "That way of paying is not available yet.");

    public static readonly Error NotOnTheMenu =
        new("order.not_on_the_menu", "One of the drinks is not on the menu any more.");

    public static readonly Error SoldOut =
        new("order.sold_out", "One of the drinks ran out.");

    public async Task<Result<ConfirmedOrder>> HandleAsync(
        ConfirmOrderCommand command,
        CancellationToken cancellationToken)
    {
        string key = (command.IdempotencyKey ?? string.Empty).Trim();

        if (key.Length == 0) return IdempotencyKeyRequired;

        // Before anything else, and before any validation: an attempt that
        // already produced an order is answered with that order, whatever the
        // menu looks like now. Criterion 6.
        if (await orders.FindByIdempotencyKeyAsync(key, cancellationToken) is Order already) return Confirmation(already);

        Result<string> name = CustomerNamePolicy.Read(command.CustomerName);

        if (!name.IsSuccess) return name.Error!;
        if (command.Lines.Count == 0) return Empty;

        IPaymentStrategy? payment = paymentStrategies.FirstOrDefault(strategy => strategy.Method == command.Method);

        if (payment is null) return PaymentMethodUnavailable;

        IReadOnlyList<Product> menu = await products.GetForOrderingAsync(
            [.. command.Lines.Select(line => line.ProductId)],
            cancellationToken);

        Result<List<NewOrderItem>> items = Price(command.Lines, menu);

        if (!items.IsSuccess) return items.Error!;

        // The code is asked for before any stock moves. The sequence lives in
        // the database and writes on its own, so asking for it with sold stock
        // already pending would let that stock reach the table before the order
        // that sold it exists.
        OrderCode code = await codes.NextAsync(cancellationToken);

        // Nothing has moved until here: every line was checked before the first
        // drink left the shelf, so a rejected order leaves the menu untouched.
        foreach (OrderLineRequest line in command.Lines)
            menu.Single(product => product.Id == line.ProductId).Take(line.Quantity);

        Order order = Order.Place(currentVenue.Id, name.Value, code, items.Value);

        payment.Pay(order);
        order.Enqueue();

        await products.SaveChangesAsync(cancellationToken);
        await orders.AddAsync(order, key, cancellationToken);

        return Confirmation(order);
    }

    /// <summary>
    /// Every line, priced from the venue's own menu. The whole order is refused
    /// on the first drink that cannot be served: she chose that on 2026-09-17,
    /// because nobody should pay for an order they will not get in full.
    /// </summary>
    private static Result<List<NewOrderItem>> Price(
        IReadOnlyCollection<OrderLineRequest> lines,
        IReadOnlyList<Product> menu)
    {
        List<NewOrderItem> items = [];

        foreach (OrderLineRequest line in lines)
        {
            Product? product = menu.FirstOrDefault(candidate => candidate.Id == line.ProductId);

            // Removed from the menu, off for tonight, or never this venue's at
            // all: the customer is told the same thing, because from where they
            // are standing it is the same thing.
            if (product is not { IsActive: true, IsAvailable: true }) return NotOnTheMenuFor(product);
            if (product.Stock < line.Quantity) return OutOfStock(product);

            items.Add(new NewOrderItem(product.Id, product.Name, product.Price, line.Quantity, line.Note));
        }

        return items;
    }

    // The name goes in the message because it is the only way the screen can
    // say which drink to take out. A product this venue never had is named by
    // nothing, which is also the honest answer.
    private static Error NotOnTheMenuFor(Product? product) => product is null
        ? NotOnTheMenu
        : new Error(NotOnTheMenu.Code, $"{product.Name} is not on the menu any more.");

    private static Error OutOfStock(Product product) =>
        new(SoldOut.Code, $"{product.Name} ran out while you were ordering.");

    private static ConfirmedOrder Confirmation(Order order) => new(
        order.Id,
        order.Code.Value,
        order.CustomerName,
        order.Total,
        order.Status,
        order.PaidAt!.Value);
}
