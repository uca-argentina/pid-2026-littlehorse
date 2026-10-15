using DrinkIt.Api.Features.Nights;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Api.Tests.Features.Tables;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Metrics;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Nights;

public class NightsEndpointsTests
{
    private const string Path = "/nights";

    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    private static readonly StaffUser MainBar = StaffUser.Create(TheVenue, "main-bar", "hash", StaffRole.Kds);
    private static readonly StaffUser Till = StaffUser.Create(TheVenue, "till-1", "hash", StaffRole.Cashier);
    private static readonly StaffUser VipBar = StaffUser.Create(TheVenue, "vip-bar", "hash", StaffRole.Kds);

    // Pins the success shape: the Angular client is generated from it.
    [Fact]
    public async Task CreateAsync_WhenTheDataIsValid_RespondsWithTheCreatedNight()
    {
        HttpResponseSnapshot response = await Create(new CreateNightRequest("Saturday 10/10", Opening, Closing, [MainBar.Id, Till.Id]));

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Equal("Saturday 10/10", response.Text("name"));
        Assert.Equal(Opening, response.Body.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(Closing, response.Body.GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(2, response.Body.GetProperty("crewIds").GetArrayLength());
        Assert.NotEqual(Guid.Empty, response.Body.GetProperty("id").GetGuid());
    }

    // The hours argue with the venue's other nights, not with the form.
    [Fact]
    public async Task CreateAsync_WhenItOverlapsAnotherNight_RespondsWithConflict()
    {
        HttpResponseSnapshot response = await Create(
            new CreateNightRequest("Saturday 10/10", Opening, Closing, [MainBar.Id, Till.Id]),
            overlaps: true);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:overlaps", response.Text("type"));
    }

    [Fact]
    public async Task CreateAsync_WhenACrewMemberIsNotInTheVenue_RespondsWithBadRequest()
    {
        HttpResponseSnapshot response = await Create(
            new CreateNightRequest("Saturday 10/10", Opening, Closing, [MainBar.Id, Till.Id, Guid.CreateVersion7()]));

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:crew-member-not-found", response.Text("type"));
    }

    [Fact]
    public async Task ListAsync_WhenTheVenueHasNights_RespondsWithThemInTheOrderGiven()
    {
        NightSummary[] stored =
        [
            new(Guid.CreateVersion7(), "Saturday 10/10", Opening, Closing, [MainBar.Id, Till.Id], new AuditInfo(null, null, null, null)),
            new(Guid.CreateVersion7(), "Friday 9/10", Opening.AddDays(-1), Closing.AddDays(-1), [MainBar.Id, Till.Id], new AuditInfo(null, null, null, null)),
        ];

        IResult result = await NightsEndpoints.ListAsync(new Fake.Queries(stored), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(2, response.Body.GetArrayLength());
        Assert.Equal("Saturday 10/10", response.Body[0].GetProperty("name").GetString());
        Assert.Equal(2, response.Body[0].GetProperty("crewIds").GetArrayLength());
    }

    [Fact]
    public async Task GetAsync_WhenTheVenueHasTheNight_RespondsWithIt()
    {
        NightSummary saturday = new(Guid.CreateVersion7(), "Saturday 10/10", Opening, Closing, [MainBar.Id, Till.Id], new AuditInfo(null, null, null, null));

        IResult result = await NightsEndpoints.GetAsync(saturday.Id, new Fake.Queries([saturday]), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{saturday.Id}", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("Saturday 10/10", response.Text("name"));
    }

    [Fact]
    public async Task GetAsync_WhenTheVenueHasNoSuchNight_RespondsWithNotFound()
    {
        Guid unknown = Guid.CreateVersion7();

        IResult result = await NightsEndpoints.GetAsync(unknown, new Fake.Queries([]), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{unknown}", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:not-found", response.Text("type"));
    }

    [Fact]
    public async Task UpdateAsync_WhenTheDataIsValid_RespondsWithTheUpdatedNight()
    {
        Night saturday = Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]);

        HttpResponseSnapshot response = await Update(saturday, new UpdateNightRequest("Saturday, late", Opening, Closing.AddHours(1), [MainBar.Id, Till.Id]));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("Saturday, late", response.Text("name"));
        Assert.Equal(Closing.AddHours(1), response.Body.GetProperty("endsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task UpdateAsync_WhenTheVenueHasNoSuchNight_RespondsWithNotFound()
    {
        HttpResponseSnapshot response = await Update(null, new UpdateNightRequest("Saturday", Opening, Closing, [MainBar.Id, Till.Id]));

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheNewHoursOverlapAnotherNight_RespondsWithConflict()
    {
        Night saturday = Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]);

        HttpResponseSnapshot response = await Update(
            saturday,
            new UpdateNightRequest("Saturday", Opening, Closing.AddHours(1), [MainBar.Id, Till.Id]),
            overlaps: true);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:overlaps", response.Text("type"));
    }

    // US-37: taking a KDS out of the night while it holds stock would leave
    // that stock stuck; the screen was opened before it was loaded.
    [Fact]
    public async Task UpdateAsync_WhenItTakesOutAKdsThatHoldsStock_RespondsWithConflict()
    {
        Night saturday = Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, VipBar, Till]);
        UpdateNightHandler handler = new(
            new Fake.Nights(overlaps: false, saturday, barHoldingStock: VipBar.Id),
            new Fake.Staff(MainBar, VipBar, Till),
            new FakeTables(),
            new NoVipAccounts(),
            new Fake.Clock(Opening.AddHours(-2)));

        IResult result = await NightsEndpoints.UpdateAsync(
            saturday.Id,
            new UpdateNightRequest("Saturday", Opening, Closing, [MainBar.Id, Till.Id]),
            handler,
            CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{saturday.Id}", HttpMethods.Put);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:bar-holds-stock", response.Text("type"));
    }

    // Pins the stock sheet's shape: the Angular client is generated from it.
    [Fact]
    public async Task GetStockAsync_WhenTheVenueHasTheNight_RespondsWithEachBarAndALinePerProduct()
    {
        Guid night = Guid.CreateVersion7();
        NightStockSheet sheet = new(night, [
            new NightStockBar(MainBar.Id, "main-bar", [new NightStockLine(Guid.CreateVersion7(), "Gin Tonic", Loaded: 12, Sold: 7, InStoreroom: 30)]),
        ]);

        IResult result = await NightsEndpoints.GetStockAsync(night, new Fake.Sheets(sheet), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{night}/stock", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        System.Text.Json.JsonElement bar = response.Body.GetProperty("bars")[0];
        Assert.Equal(MainBar.Id, bar.GetProperty("kdsId").GetGuid());
        Assert.Equal("main-bar", bar.GetProperty("name").GetString());
        System.Text.Json.JsonElement line = bar.GetProperty("lines")[0];
        Assert.Equal("Gin Tonic", line.GetProperty("productName").GetString());
        Assert.Equal(12, line.GetProperty("loaded").GetInt32());
        Assert.Equal(7, line.GetProperty("sold").GetInt32());
        Assert.Equal(5, line.GetProperty("left").GetInt32());
        Assert.Equal(30, line.GetProperty("inStoreroom").GetInt32());
    }

    [Fact]
    public async Task GetStockAsync_WhenTheVenueHasNoSuchNight_RespondsWithNotFound()
    {
        Guid unknown = Guid.CreateVersion7();

        IResult result = await NightsEndpoints.GetStockAsync(unknown, new Fake.Sheets(null), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{unknown}/stock", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:not-found", response.Text("type"));
    }

    // US-47 to US-49. Pins the report's shape: the Angular client is
    // generated from it. Times travel in whole seconds, methods by name.
    [Fact]
    public async Task GetMetricsAsync_WhenTheVenueHasTheNight_RespondsWithItsReport()
    {
        Guid night = Guid.CreateVersion7();
        Guid gin = Guid.CreateVersion7();
        DateTimeOffset paid = Opening.AddHours(1);
        NightSales sales = new(
            [new NightBar(MainBar.Id, "Barra Pista")],
            [new PaidOrder(MainBar.Id, PaymentMethod.Cash, 9000m, OrderStatus.Delivered, paid, paid.AddMinutes(6), paid.AddMinutes(9))],
            [new ProductSales(gin, "Gin Tonic", 2, 9000m)]);
        NightStockSheet sheet = new(night, [new NightStockBar(MainBar.Id, "main-bar", [new NightStockLine(gin, "Gin Tonic", Loaded: 2, Sold: 2, InStoreroom: 0)])]);

        IResult result = await NightsEndpoints.GetMetricsAsync(night, new GetNightMetricsHandler(new Fake.Sales(sales), new Fake.Sheets(sheet)), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{night}/metrics", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        System.Text.Json.JsonElement body = response.Body;
        Assert.Equal(night, body.GetProperty("nightId").GetGuid());
        Assert.Equal(9000m, body.GetProperty("sold").GetDecimal());
        Assert.Equal(1, body.GetProperty("orders").GetInt32());
        Assert.Equal(9000m, body.GetProperty("averageTicket").GetDecimal());
        Assert.Equal(2, body.GetProperty("units").GetInt32());
        System.Text.Json.JsonElement cash = body.GetProperty("byMethod")[0];
        Assert.Equal("Cash", cash.GetProperty("method").GetString());
        Assert.Equal(1, cash.GetProperty("orders").GetInt32());
        Assert.Equal(9000m, cash.GetProperty("amount").GetDecimal());
        Assert.Equal(360, body.GetProperty("paidToReady").GetProperty("averageSeconds").GetInt32());
        Assert.Equal(360, body.GetProperty("paidToReady").GetProperty("worstTenthSeconds").GetInt32());
        Assert.Equal(180, body.GetProperty("readyToDelivered").GetProperty("averageSeconds").GetInt32());
        System.Text.Json.JsonElement bar = body.GetProperty("byBar")[0];
        Assert.Equal("Barra Pista", bar.GetProperty("name").GetString());
        Assert.Equal(360, bar.GetProperty("paidToReady").GetProperty("averageSeconds").GetInt32());
        Assert.Equal(0, body.GetProperty("unmeasured").GetInt32());
        System.Text.Json.JsonElement top = body.GetProperty("top")[0];
        Assert.Equal("Gin Tonic", top.GetProperty("name").GetString());
        Assert.Equal(2, top.GetProperty("units").GetInt32());
        Assert.Equal(9000m, top.GetProperty("billed").GetDecimal());
        Assert.Equal(2, top.GetProperty("loaded").GetInt32());
        Assert.True(top.GetProperty("soldOut").GetBoolean());
        Assert.Equal(1, body.GetProperty("soldOutProducts").GetInt32());
    }

    // Nothing ready yet: no time to show, which is not zero minutes.
    [Fact]
    public async Task GetMetricsAsync_WhenNoOrderIsReadyYet_RespondsWithoutTimes()
    {
        Guid night = Guid.CreateVersion7();

        IResult result = await NightsEndpoints.GetMetricsAsync(
            night,
            new GetNightMetricsHandler(new Fake.Sales(new NightSales([], [], [])), new Fake.Sheets(new NightStockSheet(night, []))),
            CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{night}/metrics", HttpMethods.Get);

        Assert.Equal(System.Text.Json.JsonValueKind.Null, response.Body.GetProperty("paidToReady").GetProperty("averageSeconds").ValueKind);
    }

    [Fact]
    public async Task GetMetricsAsync_WhenTheVenueHasNoSuchNight_RespondsWithNotFound()
    {
        Guid unknown = Guid.CreateVersion7();

        IResult result = await NightsEndpoints.GetMetricsAsync(unknown, new GetNightMetricsHandler(new Fake.Sales(null), new Fake.Sheets(null)), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{unknown}/metrics", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:not-found", response.Text("type"));
    }

    [Fact]
    public async Task AdjustStockAsync_WhenTheChangeIsValid_RespondsWithTheSheet()
    {
        (Night saturday, Product gin) = (Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]), AGin(5));

        HttpResponseSnapshot response = await AdjustStock(saturday, gin, 12, now: Opening.AddHours(-2));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(saturday.Id, response.Body.GetProperty("nightId").GetGuid());
    }

    // Criterion 3: the screen of a night that ended is read-only, but one
    // opened before the end can still send.
    [Fact]
    public async Task AdjustStockAsync_WhenTheNightIsOver_RespondsWithConflict()
    {
        (Night saturday, Product gin) = (Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]), AGin(5));

        HttpResponseSnapshot response = await AdjustStock(saturday, gin, 12, now: Closing);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:stock-closed", response.Text("type"));
    }

    // Sold or loaded elsewhere since the screen opened: what happened
    // meanwhile, not a malformed request.
    [Fact]
    public async Task AdjustStockAsync_WhenTheStoreroomHasLessThanItLoads_RespondsWithConflict()
    {
        (Night saturday, Product gin) = (Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]), AGin(5));

        HttpResponseSnapshot response = await AdjustStock(saturday, gin, 12, now: Opening, refusal: NightErrors.StoreroomShort);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:storeroom-short", response.Text("type"));
    }

    // The bar holds less than the change takes away: a conflict with what
    // happened meanwhile, not a malformed request.
    [Fact]
    public async Task AdjustStockAsync_WhenTheBarHasLessThanItTakesAway_RespondsWithConflict()
    {
        (Night saturday, Product gin) = (Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]), AGin(2));

        HttpResponseSnapshot response = await AdjustStock(saturday, gin, -5, now: Opening, refusal: ProductErrors.StockMoved);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:stock-moved", response.Text("type"));
    }

    [Fact]
    public async Task AdjustStockAsync_WhenTheProductIsNotInThisVenue_RespondsWithNotFound()
    {
        Night saturday = Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]);

        HttpResponseSnapshot response = await AdjustStock(saturday, null, 12, now: Opening);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:product:not-found", response.Text("type"));
    }

    // Taken out of the crew while the screen was open.
    [Fact]
    public async Task AdjustStockAsync_WhenTheBarDoesNotWorkTheNight_RespondsWithConflict()
    {
        (Night saturday, Product gin) = (Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]), AGin(5));

        HttpResponseSnapshot response = await AdjustStock(saturday, gin, 12, now: Opening, bar: VipBar.Id);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:bar-not-in-night", response.Text("type"));
    }

    [Fact]
    public async Task AdjustStockAsync_WhenTheBarIsNotInThisVenue_RespondsWithNotFound()
    {
        (Night saturday, Product gin) = (Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]), AGin(5));

        HttpResponseSnapshot response = await AdjustStock(saturday, gin, 12, now: Opening, bar: Guid.CreateVersion7());

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:bar-not-found", response.Text("type"));
    }

    private static Product AGin(int stock) => Product.Create(TheVenue, "Gin Tonic", null, null, 9500m, stock, Guid.CreateVersion7());

    private static async Task<HttpResponseSnapshot> AdjustStock(
        Night night,
        Product? product,
        int change,
        DateTimeOffset now,
        Error? refusal = null,
        Guid? bar = null)
    {
        Guid productId = product?.Id ?? Guid.CreateVersion7();
        Guid kdsId = bar ?? MainBar.Id;
        AdjustNightStockHandler handler = new(
            new Fake.Nights(overlaps: false, night),
            new Fake.Products(product),
            new Fake.Staff(MainBar, VipBar, Till),
            new Fake.Shelves(refusal),
            new Fake.Sheets(new NightStockSheet(night.Id, [])),
            new Fake.Clock(now));

        IResult result = await NightsEndpoints.AdjustStockAsync(night.Id, kdsId, productId, new AdjustNightStockRequest(change), handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, $"{Path}/{night.Id}/stock/{kdsId}/{productId}/adjust", HttpMethods.Post);
    }

    private static async Task<HttpResponseSnapshot> Update(Night? stored, UpdateNightRequest request, bool overlaps = false)
    {
        Guid id = stored?.Id ?? Guid.CreateVersion7();
        UpdateNightHandler handler = new(new Fake.Nights(overlaps, stored), new Fake.Staff(MainBar, Till), new FakeTables(), new NoVipAccounts(), new Fake.Clock(Opening.AddHours(-2)));

        IResult result = await NightsEndpoints.UpdateAsync(id, request, handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, $"{Path}/{id}", HttpMethods.Put);
    }

    private static async Task<HttpResponseSnapshot> Create(CreateNightRequest request, bool overlaps = false)
    {
        CreateNightHandler handler = new(new Fake.Nights(overlaps), new Fake.Staff(MainBar, Till), new FakeTables(), new NoVipAccounts(), new Fake.CurrentVenue(), new Fake.Clock(Opening.AddHours(-2)));

        IResult result = await NightsEndpoints.CreateAsync(request, handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Post);
    }

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id => TheVenue;
        }

        public sealed class Clock(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        public sealed class Nights(bool overlaps, Night? stored = null, Guid? barHoldingStock = null) : INightRepository
        {
            public Task<bool> OverlapsAsync(DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excluding, CancellationToken cancellationToken) =>
                Task.FromResult(overlaps);

            public Task<Night?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored?.Id == id ? stored : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<IReadOnlyCollection<Guid>> BarsHoldingStockAsync(Guid nightId, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyCollection<Guid>>(barHoldingStock is Guid bar ? [bar] : []);

            public Task AddAsync(Night night, IReadOnlyList<StaffUser> bars, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public sealed class Queries(NightSummary[] stored) : INightQueries
        {
            public Task<IReadOnlyList<NightSummary>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<NightSummary>>(stored);

            public Task<NightSummary?> GetAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored.FirstOrDefault(night => night.Id == id));
        }

        public sealed class Sales(NightSales? stored) : INightMetricsQueries
        {
            public Task<NightSales?> GetAsync(Guid nightId, CancellationToken cancellationToken) => Task.FromResult(stored);
        }

        public sealed class Sheets(NightStockSheet? stored) : INightStockQueries
        {
            public Task<NightStockSheet?> GetAsync(Guid nightId, CancellationToken cancellationToken) =>
                Task.FromResult(stored?.NightId == nightId ? stored : null);
        }

        public sealed class Shelves(Error? refusal) : INightStockRepository
        {
            public Task<Result<NightStockAdjustment>> SaveAdjustmentAsync(NightStockAdjustment adjustment, DateTimeOffset now, CancellationToken cancellationToken) =>
                Task.FromResult(refusal is null ? (Result<NightStockAdjustment>)adjustment : refusal);
        }

        /// <summary>Only what loading the night's stock reads and writes.</summary>
        public sealed class Products(Product? stored) : IProductRepository
        {
            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored?.Id == id ? stored : null);

            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task AddAsync(Product product, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<bool> SaveStockAdjustmentAsync(Product product, int change, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        /// <summary>Only what creating a night and loading a bar read; the rest is never reached here.</summary>
        public sealed class Staff(params StaffUser[] stored) : IStaffUserRepository
        {
            public Task<IReadOnlyList<StaffUser>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<StaffUser>>([.. stored.Where(user => ids.Contains(user.Id))]);

            public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task AddAsync(StaffUser user, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<StaffUser?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored.FirstOrDefault(user => user.Id == id));

            public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        }
    }
}
