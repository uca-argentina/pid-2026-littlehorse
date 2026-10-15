using DrinkIt.Application.Common;
using DrinkIt.Application.Metrics;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Metrics;

/// <summary>US-47 to US-49: the report of a night, put together from its sales and its stock.</summary>
public class GetNightMetricsHandlerTests
{
    private static readonly Guid TheNight = Guid.CreateVersion7();
    private static readonly Guid Pista = Guid.CreateVersion7();
    private static readonly Guid Gin = Guid.CreateVersion7();

    [Fact]
    public async Task HandleAsync_WhenTheVenueHasNoSuchNight_IsNotFound()
    {
        GetNightMetricsHandler handler = new(new SalesOf(null), new StockOf(null));

        Result<NightMetrics> result = await handler.HandleAsync(TheNight, CancellationToken.None);

        Assert.Equal(NightErrors.NotFound, result.Error);
    }

    // The sales and the stock of the same night: what was ordered, beside what
    // its bars were loaded with.
    [Fact]
    public async Task HandleAsync_WhenTheNightExists_ReportsItsSalesAgainstItsStock()
    {
        NightSales sales = new(
            [new NightBar(Pista, "Barra Pista")],
            [new PaidOrder(Pista, PaymentMethod.Cash, 9000m, OrderStatus.Queued, DateTimeOffset.UnixEpoch, null, null)],
            [new ProductSales(Gin, "Gin Tonic", 2, 9000m)]);
        NightStockSheet stock = new(TheNight, [new NightStockBar(Pista, "Barra Pista", [new NightStockLine(Gin, "Gin Tonic", 2, 2, 0)])]);
        SalesOf queries = new(sales);

        Result<NightMetrics> result = await new GetNightMetricsHandler(queries, new StockOf(stock)).HandleAsync(TheNight, CancellationToken.None);

        Assert.Equal(9000m, result.Value.Sold);
        Assert.Equal(new TopProduct(Gin, "Gin Tonic", 2, 9000m, 2, true), result.Value.Top.Single());
        Assert.Equal(TheNight, queries.AskedFor);
    }

    private sealed class SalesOf(NightSales? sales) : INightMetricsQueries
    {
        public Guid? AskedFor { get; private set; }

        public Task<NightSales?> GetAsync(Guid nightId, CancellationToken cancellationToken)
        {
            AskedFor = nightId;

            return Task.FromResult(sales);
        }
    }

    private sealed class StockOf(NightStockSheet? sheet) : INightStockQueries
    {
        public Task<NightStockSheet?> GetAsync(Guid nightId, CancellationToken cancellationToken) => Task.FromResult(sheet);
    }
}
