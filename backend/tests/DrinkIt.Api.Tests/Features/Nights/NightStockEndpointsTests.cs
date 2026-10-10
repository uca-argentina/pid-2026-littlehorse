using DrinkIt.Api.Features.Nights;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Nights;

/// <summary>
/// US-37: the night's stock as the administration reads and corrects it. Pins
/// the shapes the Angular client is generated from.
/// </summary>
public class NightStockEndpointsTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();
    private static readonly Guid ANight = Guid.CreateVersion7();
    private static readonly Guid AProduct = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static string Path => $"/nights/{ANight}/stock";

    [Fact]
    public async Task GetStockAsync_WhenTheNightExists_RespondsWithEachProductsFigures()
    {
        FakeStocks stocks = new(AProduct, "Gin Tonic", initial: 20);

        IResult result = await NightsEndpoints.GetStockAsync(ANight, Opening(stocks), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(1, response.Body.GetArrayLength());
        Assert.Equal(AProduct, response.Body[0].GetProperty("productId").GetGuid());
        Assert.Equal("Gin Tonic", response.Body[0].GetProperty("productName").GetString());
        Assert.Equal(20, response.Body[0].GetProperty("loaded").GetInt32());
        Assert.Equal(0, response.Body[0].GetProperty("sold").GetInt32());
        Assert.Equal(20, response.Body[0].GetProperty("remaining").GetInt32());
    }

    [Fact]
    public async Task GetStockAsync_WhenNoNightOfTheVenueHasThatId_RespondsWithNotFound()
    {
        FakeStocks stocks = new(AProduct, "Gin Tonic", initial: 20) { NightExists = false };

        IResult result = await NightsEndpoints.GetStockAsync(ANight, Opening(stocks), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:not-found", response.Text("type"));
    }

    // It argues with the state of the venue's other nights, not with the request.
    [Fact]
    public async Task GetStockAsync_WhenThePreviousNightIsNotOver_RespondsWithConflict()
    {
        FakeStocks stocks = new(AProduct, "Gin Tonic", initial: 20) { PreviousEndedAt = Now.AddHours(3) };

        IResult result = await NightsEndpoints.GetStockAsync(ANight, Opening(stocks), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night-stock:previous-night-not-over", response.Text("type"));
    }

    [Fact]
    public async Task AdjustStockAsync_WhenItFits_RespondsWithTheFigures()
    {
        FakeStocks stocks = FakeStocks.WithRow(AProduct, loaded: 12);

        IResult result = await NightsEndpoints.AdjustStockAsync(
            ANight, AProduct, new AdjustNightStockRequest(8), new AdjustNightStockHandler(stocks), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/adjust", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(AProduct, response.Body.GetProperty("productId").GetGuid());
        Assert.Equal(20, response.Body.GetProperty("loaded").GetInt32());
        Assert.Equal(0, response.Body.GetProperty("sold").GetInt32());
        Assert.Equal(20, response.Body.GetProperty("remaining").GetInt32());
    }

    [Fact]
    public async Task AdjustStockAsync_WhenTheNightHasNoRowForTheProduct_RespondsWithNotFound()
    {
        FakeStocks stocks = FakeStocks.WithRow(Guid.CreateVersion7(), loaded: 12);

        IResult result = await NightsEndpoints.AdjustStockAsync(
            ANight, AProduct, new AdjustNightStockRequest(3), new AdjustNightStockHandler(stocks), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/adjust", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night-stock:not-found", response.Text("type"));
    }

    // Sales since the screen was opened: looking at the stock again is the fix.
    [Fact]
    public async Task AdjustStockAsync_WhenItWouldGoBelowZero_RespondsWithConflict()
    {
        FakeStocks stocks = FakeStocks.WithRow(AProduct, loaded: 5);

        IResult result = await NightsEndpoints.AdjustStockAsync(
            ANight, AProduct, new AdjustNightStockRequest(-6), new AdjustNightStockHandler(stocks), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path + "/adjust", HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night-stock:stock-moved", response.Text("type"));
    }

    private static OpenNightStockHandler Opening(FakeStocks stocks) =>
        new(stocks, new Venue(), new Clock(Now));

    private sealed class Venue : ICurrentVenue
    {
        public Guid Id => TheVenue;
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeStocks : INightStockRepository
    {
        private readonly List<NightStock> _rows = [];
        private readonly StockedProduct? _product;

        public FakeStocks(Guid productId, string name, int initial) =>
            _product = new StockedProduct(productId, name, initial);

        private FakeStocks()
        {
        }

        public bool NightExists { get; set; } = true;

        public DateTimeOffset? PreviousEndedAt { get; set; }

        public static FakeStocks WithRow(Guid productId, int loaded)
        {
            FakeStocks stocks = new();
            stocks._rows.Add(NightStock.Open(TheVenue, ANight, productId, loaded));

            return stocks;
        }

        public Task<NightForStock?> FindNightAsync(Guid nightId, CancellationToken cancellationToken) =>
            Task.FromResult<NightForStock?>(NightExists ? new(nightId, Now.AddHours(-1), PreviousEndedAt) : null);

        public Task<IReadOnlyList<NightStock>> ListAsync(Guid nightId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NightStock>>([.. _rows]);

        public Task<IReadOnlyList<StockedProduct>> ListActiveProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StockedProduct>>(_product is null ? [] : [_product]);

        public Task<IReadOnlyDictionary<Guid, int>> CarriedOverAsync(Guid nightId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

        public Task AddRangeAsync(IReadOnlyCollection<NightStock> rows, CancellationToken cancellationToken)
        {
            _rows.AddRange(rows);

            return Task.CompletedTask;
        }

        public Task<NightStock?> GetForUpdateAsync(Guid nightId, Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.Find(row => row.ProductId == productId));

        public Task<bool> SaveAdjustmentAsync(NightStock stock, int change, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
