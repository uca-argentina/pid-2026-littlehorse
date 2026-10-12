using DrinkIt.Api.Features.Nights;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
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

    // Pins the stock sheet's shape: the Angular client is generated from it.
    [Fact]
    public async Task GetStockAsync_WhenTheVenueHasTheNight_RespondsWithALinePerProduct()
    {
        Guid night = Guid.CreateVersion7();
        NightStockSheet sheet = new(night, [new NightStockLine(Guid.CreateVersion7(), "Gin Tonic", Carried: 20, Added: 12, Sold: 7)]);

        IResult result = await NightsEndpoints.GetStockAsync(night, new Fake.Sheets(sheet), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, $"{Path}/{night}/stock", HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        System.Text.Json.JsonElement line = response.Body.GetProperty("lines")[0];
        Assert.Equal("Gin Tonic", line.GetProperty("productName").GetString());
        Assert.Equal(20, line.GetProperty("carried").GetInt32());
        Assert.Equal(12, line.GetProperty("added").GetInt32());
        Assert.Equal(32, line.GetProperty("loaded").GetInt32());
        Assert.Equal(7, line.GetProperty("sold").GetInt32());
        Assert.Equal(25, line.GetProperty("left").GetInt32());
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

    [Fact]
    public async Task AdjustStockAsync_WhenAnEarlierNightHasNotEnded_RespondsWithConflict()
    {
        (Night saturday, Product gin) = (Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]), AGin(5));

        HttpResponseSnapshot response = await AdjustStock(saturday, gin, 12, now: Opening.AddHours(-2), earlierNightOpen: true);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:earlier-night-open", response.Text("type"));
    }

    // Sales left less than the change takes away: a conflict with what
    // happened meanwhile, not a malformed request.
    [Fact]
    public async Task AdjustStockAsync_WhenWhatIsLeftIsLessThanItTakesAway_RespondsWithConflict()
    {
        (Night saturday, Product gin) = (Night.Create(TheVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]), AGin(2));

        HttpResponseSnapshot response = await AdjustStock(saturday, gin, -5, now: Opening);

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

    private static Product AGin(int stock) => Product.Create(TheVenue, "Gin Tonic", null, null, 9500m, stock, Guid.CreateVersion7());

    private static async Task<HttpResponseSnapshot> AdjustStock(Night night, Product? product, int change, DateTimeOffset now, bool earlierNightOpen = false)
    {
        Guid productId = product?.Id ?? Guid.CreateVersion7();
        AdjustNightStockHandler handler = new(
            new Fake.Nights(overlaps: false, night, earlierNightOpen),
            new Fake.Products(product),
            new Fake.Sheets(new NightStockSheet(night.Id, [])),
            new Fake.Clock(now));

        IResult result = await NightsEndpoints.AdjustStockAsync(night.Id, productId, new AdjustNightStockRequest(change), handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, $"{Path}/{night.Id}/stock/{productId}/adjust", HttpMethods.Post);
    }

    private static async Task<HttpResponseSnapshot> Update(Night? stored, UpdateNightRequest request, bool overlaps = false)
    {
        Guid id = stored?.Id ?? Guid.CreateVersion7();
        UpdateNightHandler handler = new(new Fake.Nights(overlaps, stored), new Fake.Staff(MainBar, Till), new Fake.Clock(Opening.AddHours(-2)));

        IResult result = await NightsEndpoints.UpdateAsync(id, request, handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, $"{Path}/{id}", HttpMethods.Put);
    }

    private static async Task<HttpResponseSnapshot> Create(CreateNightRequest request, bool overlaps = false)
    {
        CreateNightHandler handler = new(new Fake.Nights(overlaps), new Fake.Staff(MainBar, Till), new Fake.CurrentVenue());

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

        public sealed class Nights(bool overlaps, Night? stored = null, bool earlierNightOpen = false) : INightRepository
        {
            public Task<bool> OverlapsAsync(DateTimeOffset startsAt, DateTimeOffset endsAt, Guid? excluding, CancellationToken cancellationToken) =>
                Task.FromResult(overlaps);

            public Task<bool> HasUnfinishedBeforeAsync(DateTimeOffset startsAt, DateTimeOffset now, CancellationToken cancellationToken) =>
                Task.FromResult(earlierNightOpen);

            public Task<Night?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored?.Id == id ? stored : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task AddAsync(Night night, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public sealed class Queries(NightSummary[] stored) : INightQueries
        {
            public Task<IReadOnlyList<NightSummary>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<NightSummary>>(stored);

            public Task<NightSummary?> GetAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored.FirstOrDefault(night => night.Id == id));
        }

        public sealed class Sheets(NightStockSheet? stored) : INightStockQueries
        {
            public Task<NightStockSheet?> GetAsync(Guid nightId, CancellationToken cancellationToken) =>
                Task.FromResult(stored?.NightId == nightId ? stored : null);
        }

        /// <summary>Only what loading the night's stock reads and writes.</summary>
        public sealed class Products(Product? stored) : IProductRepository
        {
            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored?.Id == id ? stored : null);

            public Task<bool> SaveStockAdjustmentAsync(Product product, NightStockAdjustment adjustment, CancellationToken cancellationToken) =>
                Task.FromResult(true);

            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task AddAsync(Product product, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        /// <summary>Only what creating a night reads; the rest is never reached here.</summary>
        public sealed class Staff(params StaffUser[] stored) : IStaffUserRepository
        {
            public Task<IReadOnlyList<StaffUser>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<StaffUser>>([.. stored.Where(user => ids.Contains(user.Id))]);

            public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task AddAsync(StaffUser user, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<StaffUser?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        }
    }
}
