using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Tables;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-40 as the night's form meets it: the tables a night sells, each with its
/// budget, saved with the night and read back from its own screen.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class NightTablesOverHttpTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    // Far enough ahead that no test runs into the hours of another.
    private static readonly DateTimeOffset Opening = DateTimeOffset.UtcNow.AddDays(30);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Create_WhenTablesAreChosen_TheNightListsThemWithTheirBudgets()
    {
        Venue venue = await SeedVenue();
        Crew crew = await SeedCrew(venue);
        (Table five, Table seven) = await SeedTables(venue);
        using HttpClient admin = AdminOf(venue);

        using HttpResponseMessage created = await admin.PostAsJsonAsync("/nights", ANight(crew.WithWaiter, (five, 150_000m), (seven, 90_000m)));
        string nightId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        JsonElement tables = await admin.GetFromJsonAsync<JsonElement>($"/nights/{nightId}/tables");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(2, tables.GetArrayLength());
        Assert.Equal(5, tables[0].GetProperty("number").GetInt32());
        Assert.Equal(150_000m, tables[0].GetProperty("budget").GetDecimal());
        Assert.Equal(0m, tables[0].GetProperty("spent").GetDecimal());
        Assert.Equal(7, tables[1].GetProperty("number").GetInt32());
        Assert.Equal(90_000m, tables[1].GetProperty("budget").GetDecimal());
    }

    [Fact]
    public async Task Update_WhenATableIsLeftOutAndABudgetChanges_TheNightKeepsOnlyWhatWasSent()
    {
        Venue venue = await SeedVenue();
        Crew crew = await SeedCrew(venue);
        (Table five, Table seven) = await SeedTables(venue);
        using HttpClient admin = AdminOf(venue);
        string nightId = await CreateNight(admin, ANight(crew.WithWaiter, (five, 150_000m), (seven, 90_000m)));

        using HttpResponseMessage updated = await admin.PutAsJsonAsync($"/nights/{nightId}", ANight(crew.WithWaiter, (seven, 120_000m)));
        JsonElement tables = await admin.GetFromJsonAsync<JsonElement>($"/nights/{nightId}/tables");

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        JsonElement only = Assert.Single(tables.EnumerateArray());
        Assert.Equal(7, only.GetProperty("number").GetInt32());
        Assert.Equal(120_000m, only.GetProperty("budget").GetDecimal());
    }

    // What the e2e setup does to extend tonight: it puts back the night it
    // read, which has no tables in it. That must not empty the night.
    [Fact]
    public async Task Update_WhenTheBodyHasNoTables_KeepsTheNightsTables()
    {
        Venue venue = await SeedVenue();
        Crew crew = await SeedCrew(venue);
        (Table five, _) = await SeedTables(venue);
        using HttpClient admin = AdminOf(venue);
        string nightId = await CreateNight(admin, ANight(crew.WithWaiter, (five, 150_000m)));

        using HttpResponseMessage updated = await admin.PutAsJsonAsync($"/nights/{nightId}", new
        {
            name = "Saturday, longer",
            startsAt = Opening,
            endsAt = Opening.AddHours(8),
            crewIds = crew.WithWaiter,
        });
        JsonElement tables = await admin.GetFromJsonAsync<JsonElement>($"/nights/{nightId}/tables");

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(150_000m, Assert.Single(tables.EnumerateArray()).GetProperty("budget").GetDecimal());
    }

    // US-40, criteria 1 and 4: the code the waiter hands over, and a new one
    // when it leaked.
    [Fact]
    public async Task RegenerateCode_WhenTheNightSellsTheTable_TheTablesShowTheNewCode()
    {
        Venue venue = await SeedVenue();
        Crew crew = await SeedCrew(venue);
        (Table five, _) = await SeedTables(venue);
        using HttpClient admin = AdminOf(venue);
        string nightId = await CreateNight(admin, ANight(crew.WithWaiter, (five, 150_000m)));
        string leaked = (await admin.GetFromJsonAsync<JsonElement>($"/nights/{nightId}/tables"))[0].GetProperty("accessCode").GetString()!;

        using HttpResponseMessage regenerated = await admin.PostAsync($"/nights/{nightId}/tables/{five.Id}/access-code", content: null);
        JsonElement table = (await admin.GetFromJsonAsync<JsonElement>($"/nights/{nightId}/tables"))[0];

        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        Assert.Equal(32, leaked.Length);
        Assert.NotEqual(leaked, table.GetProperty("accessCode").GetString());
        Assert.Equal(150_000m, table.GetProperty("budget").GetDecimal());
    }

    [Fact]
    public async Task RegenerateCode_WhenTheNightDoesNotSellTheTable_RespondsNotFound()
    {
        Venue venue = await SeedVenue();
        Crew crew = await SeedCrew(venue);
        (Table five, _) = await SeedTables(venue);
        using HttpClient admin = AdminOf(venue);
        string nightId = await CreateNight(admin, ANight(crew.WithWaiter));

        using HttpResponseMessage response = await admin.PostAsync($"/nights/{nightId}/tables/{five.Id}/access-code", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenTablesAreChosenWithoutAWaiter_RespondsWithTheWaiterProblem()
    {
        Venue venue = await SeedVenue();
        Crew crew = await SeedCrew(venue);
        (Table five, _) = await SeedTables(venue);
        using HttpClient admin = AdminOf(venue);

        using HttpResponseMessage response = await admin.PostAsJsonAsync("/nights", ANight(crew.WithoutWaiter, (five, 150_000m)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:waiter-required", await TypeOf(response));
    }

    // The venue comes from the token: the table next door is not found, not refused.
    [Fact]
    public async Task Create_WhenATableBelongsToAnotherVenue_RespondsTableNotFound()
    {
        Venue mine = await SeedVenue();
        Venue theirs = await SeedVenue();
        Crew crew = await SeedCrew(mine);
        (Table foreign, _) = await SeedTables(theirs);
        using HttpClient admin = AdminOf(mine);

        using HttpResponseMessage response = await admin.PostAsJsonAsync("/nights", ANight(crew.WithWaiter, (foreign, 150_000m)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:night:table-not-found", await TypeOf(response));
    }

    // US-39's edge: somebody bought the table for a night still to come.
    [Fact]
    public async Task Deactivate_WhenTheTableIsInANightNotOver_RespondsWithConflict()
    {
        Venue venue = await SeedVenue();
        Crew crew = await SeedCrew(venue);
        (Table five, _) = await SeedTables(venue);
        using HttpClient admin = AdminOf(venue);
        await CreateNight(admin, ANight(crew.WithWaiter, (five, 150_000m)));

        using HttpResponseMessage response = await admin.PostAsync($"/staff/tables/{five.Id}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:table:in-a-night-not-over", await TypeOf(response));
    }

    [Fact]
    public async Task GetTables_WhenTheNightBelongsToAnotherVenue_RespondsNotFound()
    {
        Venue mine = await SeedVenue();
        Venue theirs = await SeedVenue();
        Crew crew = await SeedCrew(theirs);
        using HttpClient theirAdmin = AdminOf(theirs);
        string nightId = await CreateNight(theirAdmin, ANight(crew.WithWaiter));
        using HttpClient myAdmin = AdminOf(mine);

        using HttpResponseMessage response = await myAdmin.GetAsync($"/nights/{nightId}/tables");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record Crew(Guid[] WithWaiter, Guid[] WithoutWaiter);

    private static object ANight(Guid[] crewIds, params (Table Table, decimal Budget)[] tables) => new
    {
        name = "Saturday",
        startsAt = Opening,
        endsAt = Opening.AddHours(7),
        crewIds,
        tables = tables.Select(chosen => new { tableId = chosen.Table.Id, budget = chosen.Budget }).ToArray(),
    };

    private static async Task<string> CreateNight(HttpClient admin, object night)
    {
        using HttpResponseMessage created = await admin.PostAsJsonAsync("/nights", night);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task<string?> TypeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString();

    private async Task<Venue> SeedVenue()
    {
        Venue venue = Venue.Create("Test Bar", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        await seed.SaveChangesAsync();

        return venue;
    }

    private async Task<Crew> SeedCrew(Venue venue)
    {
        StaffUser bar = StaffUser.Create(venue.Id, $"bar-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Kds);
        StaffUser till = StaffUser.Create(venue.Id, $"till-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Cashier);
        StaffUser waiter = StaffUser.Create(venue.Id, $"waiter-{Guid.NewGuid():N}"[..20], "hash", StaffRole.Waiter);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.StaffUsers.AddRange(bar, till, waiter);
        await seed.SaveChangesAsync();

        return new Crew([bar.Id, till.Id, waiter.Id], [bar.Id, till.Id]);
    }

    private async Task<(Table Five, Table Seven)> SeedTables(Venue venue)
    {
        Table five = Table.Create(venue.Id, 5, 8, null, DateTimeOffset.UtcNow);
        Table seven = Table.Create(venue.Id, 7, 10, "Upstairs", DateTimeOffset.UtcNow);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Tables.AddRange(five, seven);
        await seed.SaveChangesAsync();

        return (five, seven);
    }

    private HttpClient AdminOf(Venue venue)
    {
        AccessToken token = _factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(Guid.CreateVersion7(), venue.Id, "admin", StaffRole.Administrator);

        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        return client;
    }
}
