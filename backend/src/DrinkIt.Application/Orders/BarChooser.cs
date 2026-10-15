namespace DrinkIt.Application.Orders;

/// <summary>
/// Which bar an order goes to (US-37): the one with the least work among those
/// holding all of it. An order is never split between two bars, because it is
/// called for and handed over in one go.
/// </summary>
public static class BarChooser
{
    /// <summary>
    /// Null when no single bar has it all. Among those that do, a bar whose
    /// tablet is connected wins over one that is not (US-38), and only then
    /// does less work decide. With none of them connected the order still goes
    /// to one, and waits on that tablet: refused or left without a bar, the
    /// customer would lose it over something they cannot see. Equal work goes
    /// to the first given, so asking twice answers the same.
    /// </summary>
    public static BarSupply? Choose(IReadOnlyList<BarSupply> bars, IReadOnlyCollection<OrderLine> lines, DateTimeOffset now) =>
        bars
            .Where(bar => lines.All(line => bar.UnitsOf(line.ProductId) >= line.Quantity))
            .OrderByDescending(bar => bar.IsConnectedAt(now))
            .ThenBy(bar => bar.Load)
            .FirstOrDefault();

    /// <summary>
    /// The drink to name when no bar has it all: one that no bar has enough of
    /// or, when each is somewhere but not together, what the bar that came
    /// closest lacks. The customer takes that one out and tries again.
    /// </summary>
    public static Guid Missing(IReadOnlyList<BarSupply> bars, IReadOnlyCollection<OrderLine> lines)
    {
        OrderLine? nowhere = lines.FirstOrDefault(line => !bars.Any(bar => bar.UnitsOf(line.ProductId) >= line.Quantity));

        if (nowhere is not null) return nowhere.ProductId;

        BarSupply closest = bars.MaxBy(bar => lines.Count(line => bar.UnitsOf(line.ProductId) >= line.Quantity))!;

        return lines.First(line => closest.UnitsOf(line.ProductId) < line.Quantity).ProductId;
    }
}
