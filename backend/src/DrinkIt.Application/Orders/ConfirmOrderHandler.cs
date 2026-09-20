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

/// <summary>
/// The order as the confirmation screen shows it.
/// </summary>
/// <remarks>
/// <see cref="TrackingToken"/> is handed over exactly once, here, in the answer
/// to the request that created the order: it is the only way the customer's
/// phone can build the link that lets them watch it. Every later answer about
/// this order leaves it out.
/// </remarks>
public sealed record ConfirmedOrder(
    Guid Id,
    string Code,
    string TrackingToken,
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

    public static readonly Error DuplicateLine =
        new("order.duplicate_line", "Each drink goes on one line, with a quantity beside it.");

    public static readonly Error QuantityNotPositive =
        new("order.quantity_not_positive", "Every drink in the order needs a quantity of at least one.");

    /// <summary>
    /// How many times an order will be rebuilt against a menu that moved
    /// underneath it. A packed venue has everybody ordering the same three
    /// drinks at once, so losing a race is ordinary and must not reach anybody:
    /// what must reach them is running out, which is a different answer.
    /// </summary>
    private const int Attempts = 5;

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

        // Before the code is asked for, because asking for it spends one. Two
        // lines of the same drink also make the stock check below read the same
        // number twice and let through more than there is.
        if (command.Lines.Select(line => line.ProductId).Distinct().Count() != command.Lines.Count) return DuplicateLine;

        // A line asking for none of something, or for minus one. The screen
        // cannot produce it, a hand-rolled request can, and nothing is ever
        // less than zero in stock — so it would slip past the check below and
        // break inside the domain, with a problem type written for us.
        if (command.Lines.Any(line => line.Quantity <= 0)) return QuantityNotPositive;

        IPaymentStrategy? payment = paymentStrategies.FirstOrDefault(strategy => strategy.Method == command.Method);

        if (payment is null) return PaymentMethodUnavailable;

        // The code is asked for once, before any stock moves and outside the
        // loop below. The sequence lives in the database and writes on its own,
        // so asking for it with sold stock already pending would let that stock
        // reach the table before the order that sold it exists — and asking
        // again on every attempt would burn a code per attempt.
        OrderCode code = await codes.NextAsync(cancellationToken);

        Guid[] wanted = [.. command.Lines.Select(line => line.ProductId)];
        Result<ConfirmedOrder> outcome = OrderErrors.StockMoved;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            outcome = await TryToConfirm(command, wanted, name.Value, code, payment, cancellationToken);

            if (outcome.IsSuccess || outcome.Error!.Code != OrderErrors.StockMoved.Code) return outcome;
        }

        // Every attempt lost the race. Somebody is hammering the same drink and
        // the honest answer is to look at the order again, not to keep trying.
        return outcome;
    }

    /// <summary>
    /// One go at it: read the menu as it is now, price the order against it,
    /// take the drinks and write. Refused when the stock moved between the read
    /// and the write, which the caller answers by going round again with a menu
    /// that has moved too.
    /// </summary>
    private async Task<Result<ConfirmedOrder>> TryToConfirm(
        ConfirmOrderCommand command,
        Guid[] wanted,
        string customerName,
        OrderCode code,
        IPaymentStrategy payment,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Product> menu = await products.GetForOrderingAsync(wanted, cancellationToken);

        Result<List<NewOrderItem>> items = Price(command.Lines, menu);

        if (!items.IsSuccess) return items.Error!;

        Order order = Order.Place(currentVenue.Id, customerName, code, items.Value);

        // How far this goes is the payment method's call, not this handler's:
        // digital settles and queues, cash will stop at the till.
        payment.Settle(order);

        // The one write of the use case: the order and the stock it sold. It
        // can come back refused, because somebody else sold from the same
        // product while this order was being priced, or carrying the order that
        // a retry of this very request wrote first.
        Result<Order> written = await orders.AddAsync(order, TheKeyOf(command), cancellationToken);

        return written.IsSuccess ? Confirmation(written.Value) : written.Error!;
    }

    /// <summary>
    /// Every line, priced from the venue's own menu. The whole order is refused
    /// on the first drink that cannot be served: she chose that on 2026-09-17,
    /// because nobody should pay for an order they will not get in full.
    /// </summary>
    /// <remarks>
    /// The stock read here is a few seconds old by the time anything is
    /// written, which is what makes this a message and not a guarantee: it
    /// exists so the customer is told which drink ran out, in a sentence. What
    /// actually stops one drink being sold twice is the conditional update in
    /// the repository.
    /// </remarks>
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

    private static string TheKeyOf(ConfirmOrderCommand command) =>
        (command.IdempotencyKey ?? string.Empty).Trim();

    private static ConfirmedOrder Confirmation(Order order) => new(
        order.Id,
        order.Code.Value,
        order.TrackingToken.Value,
        order.CustomerName,
        order.Total,
        order.Status,
        order.PaidAt!.Value);
}
