using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Cashier;

/// <summary>
/// US-26: the cashier took the money for the order behind this code. Paid now,
/// and handed to the bar with the least work among those that have all of it
/// (US-36) — saving it takes the drinks from that bar and raises what puts it
/// on its board.
/// </summary>
/// <remarks>
/// Collecting what is not waiting for money is answered, not thrown: a second
/// tap, or a customer showing the same code twice, is an ordinary night at the
/// till (criterion 3), and the screen has to say so in words.
/// </remarks>
public sealed class CollectCashHandler(
    IOrderRepository orders,
    IBarsForOrdering bars,
    TimeProvider clock,
    ICurrentStaffUser cashier)
{
    /// <summary>The same reasoning as in ConfirmOrderHandler: losing a race is ordinary, running out is not.</summary>
    private const int Attempts = 5;

    public async Task<Result<OrderStatus>> HandleAsync(string code, CancellationToken cancellationToken)
    {
        if (!OrderCode.TryParse(code, out OrderCode? parsed)) return CashierErrors.OrderNotFound;

        Result<OrderStatus> outcome = OrderErrors.StockMoved;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            outcome = await TryToCollect(parsed!, cancellationToken);

            if (outcome.IsSuccess || outcome.Error!.Code != OrderErrors.StockMoved.Code) return outcome;
        }

        return outcome;
    }

    private async Task<Result<OrderStatus>> TryToCollect(OrderCode code, CancellationToken cancellationToken)
    {
        Order? order = await orders.GetForUpdateAsync(code, cancellationToken);

        if (order is null) return CashierErrors.OrderNotFound;
        if (order.Status != OrderStatus.AwaitingPayment) return CashierErrors.AlreadyPaid;

        // An order from before nights existed has no night and so no bar to
        // go to: it reads as nothing anywhere, the same answer as running out.
        OrderLine[] lines = [.. order.Items.Select(item => new OrderLine(item.ProductId, item.Quantity))];
        IReadOnlyList<BarSupply> nightsBars = await bars.GetAsync(
            order.NightId ?? Guid.Empty,
            [.. lines.Select(line => line.ProductId)],
            cancellationToken);
        BarSupply? bar = BarChooser.Choose(nightsBars, lines);

        if (bar is null) return SoldOut(order, BarChooser.Missing(nightsBars, lines));

        // The till's route demands a signed-in cashier, so there is always a
        // name here; the domain refuses the order rather than a blank one.
        order.CollectCash(clock.GetUtcNow(), cashier.Username ?? string.Empty, bar.KdsId);

        Result<Order> saved = await orders.SaveCollectionAsync(order, cancellationToken);

        if (saved.IsSuccess) return order.Status;

        // Another till collected it in the same instant and got there first.
        // Waiting for cash, collecting is the only thing anybody can do to it,
        // so for this till it is simply already paid. A sale that took the
        // stock is answered by the caller going round again.
        return saved.Error!.Code == OrderErrors.StockMoved.Code ? saved.Error : CashierErrors.AlreadyPaid;
    }

    // Named from the order's own line: the drink is the one the customer
    // asked for, whatever the menu calls it now.
    private static Error SoldOut(Order order, Guid missing)
    {
        string name = order.Items.First(item => item.ProductId == missing).ProductName;

        return new Error(CashierErrors.SoldOut.Code, $"{name} ran out at every bar.", name);
    }
}
