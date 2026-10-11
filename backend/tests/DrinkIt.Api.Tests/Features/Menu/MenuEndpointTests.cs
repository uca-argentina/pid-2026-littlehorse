using DrinkIt.Api.Features.Menu;
using DrinkIt.Api.Tenancy;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Nights;
using DrinkIt.Application.Venues;
using DrinkIt.Domain.Menu;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Menu;

/// <summary>
/// US-09. The only endpoint of the app that answers without a token, so the
/// shape of what it sends back is the contract a stranger's phone gets.
/// </summary>
public class MenuEndpointTests
{
    private const string Path = "/bar-alfa/menu";

    private static readonly Guid TheCategory = Guid.CreateVersion7();

    private static readonly Guid TheNight = Guid.CreateVersion7();

    private static readonly MenuItem GinTonic = new(
        Guid.CreateVersion7(), "Gin Tonic", "Gin, tónica, lima", "https://images.example.com/gin.png", 4500m,
        TheCategory, true);

    private static readonly MenuItem Aperol = new(
        Guid.CreateVersion7(), "Aperol Spritz", null, null, 6000m, TheCategory, false);

    // Pins the shape: the Angular client is generated from it, so renaming a
    // property here breaks the customer's screen silently.
    [Fact]
    public async Task GetAsync_WhenTheVenueSellsSomething_RespondsWithItsMenu()
    {
        HttpResponseSnapshot response = await Menu(GinTonic, Aperol);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal(2, response.Body.GetProperty("items").GetArrayLength());
        Assert.Equal("Gin Tonic", response.Body.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(4500m, response.Body.GetProperty("items")[0].GetProperty("price").GetDecimal());
        Assert.Equal(TheCategory, response.Body.GetProperty("items")[0].GetProperty("categoryId").GetGuid());
        Assert.False(response.Body.GetProperty("items")[1].GetProperty("isOrderable").GetBoolean());
    }

    // The header says which venue this is, and the customer scanned a QR: they
    // never typed the name, so the screen has to be the one that tells them.
    [Fact]
    public async Task GetAsync_WhenTheVenueExists_NamesIt()
    {
        HttpResponseSnapshot response = await Menu(GinTonic);

        Assert.Equal("Bar Alfa", response.Text("venueName"));
    }

    // Criterion 5 is the screen's job, but it can only do it if an empty venue
    // answers with an empty menu rather than with a failure.
    [Fact]
    public async Task GetAsync_WhenNothingIsLoadedYet_RespondsWithAnEmptyMenu()
    {
        HttpResponseSnapshot response = await Menu();

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(0, response.Body.GetProperty("items").GetArrayLength());
        Assert.Equal("Bar Alfa", response.Text("venueName"));
    }

    /// <summary>
    /// A mistyped or invented slug. Answering 404 keeps it apart from a venue
    /// that exists and has nothing loaded, which is criterion 5 and reads
    /// completely differently on the phone.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenNoVenueHasThatSlug_RespondsWithNotFound()
    {
        IResult result = await MenuEndpoint.GetAsync(
            "bar-que-no-existe",
            new CurrentVenue(),
            new Fake.Menu(),
            new FakeCategoryQueries(),
            new Fake.Tonight(true),
            new OpenNightStockHandler(new NightStocksForAnyNight(), new Fake.Venue(), TimeProvider.System),
            TimeProvider.System,
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:venue:not-found", response.Text("type"));
    }

    // US-14: the tabs are the venue's own, so they travel with the menu, in the
    // order the venue made them.
    [Fact]
    public async Task GetAsync_WhenTheVenueHasCategories_SendsThemInOrder()
    {
        FakeCategoryQueries categories = new(
            new CategoryListItem(TheCategory, "Tragos"),
            new CategoryListItem(Guid.CreateVersion7(), "Cervezas"));

        HttpResponseSnapshot response = await MenuWith(categories, GinTonic);

        Assert.Equal(2, response.Body.GetProperty("categories").GetArrayLength());
        Assert.Equal("Tragos", response.Body.GetProperty("categories")[0].GetProperty("name").GetString());
        Assert.Equal(TheCategory, response.Body.GetProperty("categories")[0].GetProperty("id").GetGuid());
        Assert.Equal("Cervezas", response.Body.GetProperty("categories")[1].GetProperty("name").GetString());
    }

    // US-35, criterion 3: the menu is still readable with no night on, and
    // says so, so the phone warns before anybody puts an order together.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAsync_Always_SaysWhetherTheVenueIsTakingOrders(bool nightIsOn)
    {
        HttpResponseSnapshot response = await MenuWith(new FakeCategoryQueries(), nightIsOn, GinTonic);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(nightIsOn, response.Body.GetProperty("isTakingOrders").GetBoolean());
        Assert.Equal(1, response.Body.GetProperty("items").GetArrayLength());
    }

    // Nothing about how many are left leaves the building.
    [Fact]
    public async Task GetAsync_WhenTheVenueSellsSomething_SendsNoStockCount()
    {
        HttpResponseSnapshot response = await Menu(GinTonic, Aperol);

        Assert.DoesNotContain("stock", response.Raw, StringComparison.OrdinalIgnoreCase);
    }

    // US-37: "sold out" is what the night has left, so the menu is read about
    // the night that is on, and about nothing when none is.
    [Fact]
    public async Task GetAsync_WhenANightIsOn_ReadsTheMenuAboutThatNight()
    {
        Fake.Menu menu = new(GinTonic);

        await Read(menu, new NightStocksForAnyNight(), nightIsOn: true);

        Assert.Equal(TheNight, menu.AskedAbout);
    }

    [Fact]
    public async Task GetAsync_WhenNoNightIsOn_ReadsTheMenuAboutNoNight()
    {
        Fake.Menu menu = new(GinTonic);

        await Read(menu, new NightStocksForAnyNight(), nightIsOn: false);

        Assert.Null(menu.AskedAbout);
    }

    // Nobody opens a night's stock by hand for the menu to be right: the
    // first phone that looks at it is what makes it exist.
    [Fact]
    public async Task GetAsync_WhenANightIsOn_OpensItsStockBeforeReadingTheMenu()
    {
        Product gin = Product.Create(Guid.CreateVersion7(), "Gin Tonic", null, null, 4500m, 20, TheCategory);
        NightStocksForAnyNight stocks = new(gin);

        await Read(new Fake.Menu(GinTonic), stocks, nightIsOn: true);

        Assert.Equal(gin.Id, stocks.Rows.Single().ProductId);
        Assert.Equal(TheNight, stocks.Rows.Single().NightId);
    }

    private static async Task<HttpResponseSnapshot> Menu(params MenuItem[] items) =>
        await MenuWith(new FakeCategoryQueries(), nightIsOn: true, items);

    private static async Task<HttpResponseSnapshot> MenuWith(FakeCategoryQueries categories, params MenuItem[] items) =>
        await MenuWith(categories, nightIsOn: true, items);

    private static async Task<HttpResponseSnapshot> MenuWith(FakeCategoryQueries categories, bool nightIsOn, params MenuItem[] items) =>
        await Read(new Fake.Menu(items), new NightStocksForAnyNight(), nightIsOn, categories);

    private static async Task<HttpResponseSnapshot> Read(
        Fake.Menu menu,
        NightStocksForAnyNight stocks,
        bool nightIsOn,
        FakeCategoryQueries? categories = null)
    {
        CurrentVenue venue = new();
        venue.Resolve(new VenueIdentity(Guid.CreateVersion7(), "Bar Alfa", "bar-alfa"));

        IResult result = await MenuEndpoint.GetAsync(
            "bar-alfa",
            venue,
            menu,
            categories ?? new FakeCategoryQueries(),
            new Fake.Tonight(nightIsOn),
            new OpenNightStockHandler(stocks, new Fake.Venue(), TimeProvider.System),
            TimeProvider.System,
            CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Get);
    }

    private static class Fake
    {
        public sealed class Venue : Application.Common.ICurrentVenue
        {
            public Guid Id { get; } = Guid.CreateVersion7();
        }

        public sealed class Tonight(bool isOn) : IUnderwayNightLookup
        {
            public Task<Guid?> FindIdAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
                Task.FromResult(isOn ? TheNight : (Guid?)null);
        }

        public sealed class Menu(params MenuItem[] items) : IProductQueries
        {
            /// <summary>The night the menu was read about, null when none was on.</summary>
            public Guid? AskedAbout { get; private set; }

            public Task<IReadOnlyList<ProductListItem>> ListAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException("The customer's menu never reads the administration listing.");

            public Task<IReadOnlyList<MenuItem>> ListForMenuAsync(Guid? night, CancellationToken cancellationToken)
            {
                AskedAbout = night;

                return Task.FromResult<IReadOnlyList<MenuItem>>(items);
            }
        }
    }
}
