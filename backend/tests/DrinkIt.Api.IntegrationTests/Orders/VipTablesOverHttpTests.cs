using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using DrinkIt.Domain.VipAccounts;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// US-41 as the customer's phone meets it: the QR of a VIP table opens its
/// menu with the balance, and an order from it is paid from that balance and
/// reaches the bar saying which table.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class VipTablesOverHttpTests(SqlServerFixture sql) : IAsyncDisposable
{
    private const string NotFoundType = "urn:drinkit:problem:vip-account:not-found";

    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    // Criterion 1: "Mesa 5" and the balance, before ordering anything.
    [Fact]
    public async Task FindVipTable_WhenTheCodeIsTonights_RespondsWithTheTableAndItsBalance()
    {
        Seeded seeded = await AVenueWithATableTonight(budget: 20_000m);
        using HttpClient phone = _factory.CreateClient();

        JsonElement table = await phone.GetFromJsonAsync<JsonElement>($"/{seeded.Venue.Slug}/vip-accounts/{seeded.AccessCode}");

        Assert.Equal(5, table.GetProperty("tableNumber").GetInt32());
        Assert.Equal(20_000m, table.GetProperty("balance").GetDecimal());
    }

    // Criterion 4: invented, or of the venue next door, read the same.
    [Fact]
    public async Task FindVipTable_WhenTheCodeOpensNothingHere_RespondsNotFound()
    {
        Seeded mine = await AVenueWithATableTonight(budget: 20_000m);
        Seeded theirs = await AVenueWithATableTonight(budget: 20_000m);
        using HttpClient phone = _factory.CreateClient();

        using HttpResponseMessage invented = await phone.GetAsync($"/{mine.Venue.Slug}/vip-accounts/{new string('0', 32)}");
        using HttpResponseMessage foreign = await phone.GetAsync($"/{mine.Venue.Slug}/vip-accounts/{theirs.AccessCode}");

        Assert.Equal(HttpStatusCode.NotFound, invented.StatusCode);
        Assert.Equal(NotFoundType, await TypeOf(invented));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(NotFoundType, await TypeOf(foreign));
    }

    // Criterion 2: the balance pays, nobody chooses a method, and the bar sees "Mesa 5".
    [Fact]
    public async Task Confirm_WithTheTablesCode_PaysFromTheBalanceAndTheBarSeesTheTable()
    {
        Seeded seeded = await AVenueWithATableTonight(budget: 20_000m);
        using HttpClient phone = _factory.CreateClient();

        using HttpResponseMessage confirmed = await phone.PostAsJsonAsync($"/{seeded.Venue.Slug}/orders", AnOrderFrom(seeded));
        JsonElement table = await phone.GetFromJsonAsync<JsonElement>($"/{seeded.Venue.Slug}/vip-accounts/{seeded.AccessCode}");
        JsonElement queue = await BarOf(seeded).GetFromJsonAsync<JsonElement>("/kds/queue");

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        Assert.Equal(11_000m, table.GetProperty("balance").GetDecimal());
        JsonElement card = Assert.Single(queue.EnumerateArray());
        Assert.True(card.GetProperty("isForTable").GetBoolean());
        Assert.Equal(5, card.GetProperty("tableNumber").GetInt32());
    }

    // US-42, criterion 2: the phone pays what the balance does not cover, the
    // balance ends at zero and the bar gets the order.
    [Fact]
    public async Task Confirm_PayingTheDifference_EmptiesTheBalanceAndTheBarGetsIt()
    {
        Seeded seeded = await AVenueWithATableTonight(budget: 5_000m);
        using HttpClient phone = _factory.CreateClient();

        using HttpResponseMessage confirmed = await phone.PostAsJsonAsync($"/{seeded.Venue.Slug}/orders", AnOrderFrom(seeded, digitalAmount: 4_000m));
        JsonElement table = await phone.GetFromJsonAsync<JsonElement>($"/{seeded.Venue.Slug}/vip-accounts/{seeded.AccessCode}");
        JsonElement queue = await BarOf(seeded).GetFromJsonAsync<JsonElement>("/kds/queue");

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        Assert.Equal(0m, table.GetProperty("balance").GetDecimal());
        Assert.Equal(5, Assert.Single(queue.EnumerateArray()).GetProperty("tableNumber").GetInt32());
    }

    // US-42, the edge: the difference the phone showed is not the difference
    // any more. 409 before charging anything, and the balance as it was.
    [Fact]
    public async Task Confirm_WhenTheDifferenceChanged_RespondsConflictAndSpendsNothing()
    {
        Seeded seeded = await AVenueWithATableTonight(budget: 5_000m);
        using HttpClient phone = _factory.CreateClient();

        using HttpResponseMessage response = await phone.PostAsJsonAsync($"/{seeded.Venue.Slug}/orders", AnOrderFrom(seeded));
        JsonElement table = await phone.GetFromJsonAsync<JsonElement>($"/{seeded.Venue.Slug}/vip-accounts/{seeded.AccessCode}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:vip-account:balance-changed", await TypeOf(response));
        Assert.Equal(5_000m, table.GetProperty("balance").GetDecimal());
    }

    private sealed record Seeded(Venue Venue, Product Gin, StaffUser Bar, string AccessCode);

    /// <summary>Two gin tonics at 4500: 9000.</summary>
    private static object AnOrderFrom(Seeded seeded, decimal? digitalAmount = null) => new
    {
        customerName = "María Quadro",
        method = "VipBalance",
        idempotencyKey = Guid.NewGuid().ToString(),
        lines = new[] { new { productId = seeded.Gin.Id, quantity = 2 } },
        tableCode = seeded.AccessCode,
        digitalAmount,
    };

    private static async Task<string?> TypeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString();

    private HttpClient BarOf(Seeded seeded)
    {
        AccessToken token = _factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(seeded.Bar.Id, seeded.Venue.Id, seeded.Bar.Username, StaffRole.Kds);

        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        return client;
    }

    /// <summary>A night on right now, with a gin in its bar and table 5 sold for it.</summary>
    private async Task<Seeded> AVenueWithATableTonight(decimal budget)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Venue venue = Venue.Create("Test Bar", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 100, category.Id);
        StaffUser bar = StaffUser.Create(venue.Id, "main-bar", "hash", StaffRole.Kds);
        StaffUser[] crew = [bar, StaffUser.Create(venue.Id, "till-1", "hash", StaffRole.Cashier), StaffUser.Create(venue.Id, "martin", "hash", StaffRole.Waiter)];
        Night tonight = Night.Create(venue.Id, "Tonight", now.AddHours(-1), now.AddHours(5), crew);
        Table five = Table.Create(venue.Id, 5, 8, null, now.AddDays(-1));
        VipAccount account = VipAccount.Open(tonight, five, budget, now.AddHours(-2));

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(gin);
        seed.StaffUsers.AddRange(crew);
        seed.Nights.Add(tonight);
        seed.Tables.Add(five);
        seed.VipAccounts.Add(account);
        seed.BarStocks.Add(new BarStock { VenueId = venue.Id, NightId = tonight.Id, KdsId = bar.Id, ProductId = gin.Id, Units = 100 });
        await seed.SaveChangesAsync();

        return new Seeded(venue, gin, bar, account.AccessCode);
    }
}
