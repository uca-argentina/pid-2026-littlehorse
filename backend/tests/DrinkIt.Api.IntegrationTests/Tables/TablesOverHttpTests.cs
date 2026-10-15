using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Tables;

/// <summary>
/// The table administration as the PWA reaches it: through the router, the
/// token and the venue filter. What a unit test of the endpoint cannot show is
/// who gets in and which venue's rows they touch.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class TablesOverHttpTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Create_WhenTheAdministratorAddsATable_ItShowsUpInTheListing()
    {
        Venue venue = await SeedVenue();
        using HttpClient admin = ClientFor(venue, StaffRole.Administrator);

        using HttpResponseMessage created = await admin.PostAsJsonAsync("/staff/tables", new { number = 5 });
        JsonElement listed = await admin.GetFromJsonAsync<JsonElement>("/staff/tables");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(5, listed[0].GetProperty("number").GetInt32());
        Assert.True(listed[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Create_WhenTheNumberIsRepeated_RespondsWithConflict()
    {
        Venue venue = await SeedVenue();
        using HttpClient admin = ClientFor(venue, StaffRole.Administrator);
        await admin.PostAsJsonAsync("/staff/tables", new { number = 5 });

        using HttpResponseMessage again = await admin.PostAsJsonAsync("/staff/tables", new { number = 5 });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    // The number of a table put away is still taken: creating it again is
    // refused, and bringing it back is what the screen offers instead.
    [Fact]
    public async Task Create_WhenTheTableWithThatNumberWasPutAway_RespondsWithConflictUntilItIsReactivated()
    {
        Venue venue = await SeedVenue();
        using HttpClient admin = ClientFor(venue, StaffRole.Administrator);
        JsonElement table = await (await admin.PostAsJsonAsync("/staff/tables", new { number = 5 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        string id = table.GetProperty("id").GetString()!;

        using HttpResponseMessage away = await admin.PostAsync($"/staff/tables/{id}/deactivate", content: null);
        using HttpResponseMessage again = await admin.PostAsJsonAsync("/staff/tables", new { number = 5 });
        using HttpResponseMessage back = await admin.PostAsync($"/staff/tables/{id}/reactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, away.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.True((await back.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task Create_WhenTheNumberIsOutOfRange_RespondsWithBadRequest(int number)
    {
        Venue venue = await SeedVenue();
        using HttpClient admin = ClientFor(venue, StaffRole.Administrator);

        using HttpResponseMessage response = await admin.PostAsJsonAsync("/staff/tables", new { number });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRole.Cashier)]
    [InlineData(StaffRole.Waiter)]
    [InlineData(StaffRole.Kds)]
    public async Task Create_WhenTheTokenIsNotAnAdministrator_IsForbidden(StaffRole role)
    {
        Venue venue = await SeedVenue();
        using HttpClient staff = ClientFor(venue, role);

        using HttpResponseMessage response = await staff.PostAsJsonAsync("/staff/tables", new { number = 5 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenThereIsNoToken_IsUnauthorized()
    {
        using HttpClient anonymous = _factory.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync("/staff/tables");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The venue comes from the token and nothing else: an administrator of the
    // venue next door finds neither the listing nor the id.
    [Fact]
    public async Task Deactivate_WhenTheTableBelongsToAnotherVenue_RespondsNotFoundAndLeavesItAlone()
    {
        Venue mine = await SeedVenue();
        Venue theirs = await SeedVenue();
        using HttpClient myAdmin = ClientFor(mine, StaffRole.Administrator);
        using HttpClient theirAdmin = ClientFor(theirs, StaffRole.Administrator);
        JsonElement table = await (await theirAdmin.PostAsJsonAsync("/staff/tables", new { number = 5 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        string id = table.GetProperty("id").GetString()!;

        using HttpResponseMessage response = await myAdmin.PostAsync($"/staff/tables/{id}/deactivate", content: null);
        JsonElement mineListed = await myAdmin.GetFromJsonAsync<JsonElement>("/staff/tables");
        JsonElement theirsListed = await theirAdmin.GetFromJsonAsync<JsonElement>("/staff/tables");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, mineListed.GetArrayLength());
        Assert.True(theirsListed[0].GetProperty("isActive").GetBoolean());
    }

    // Two venues each have their own "Mesa 5".
    [Fact]
    public async Task Create_WhenAnotherVenueHasThatNumber_StillCreatesIt()
    {
        Venue mine = await SeedVenue();
        Venue theirs = await SeedVenue();
        using HttpClient myAdmin = ClientFor(mine, StaffRole.Administrator);
        using HttpClient theirAdmin = ClientFor(theirs, StaffRole.Administrator);
        await theirAdmin.PostAsJsonAsync("/staff/tables", new { number = 5 });

        using HttpResponseMessage response = await myAdmin.PostAsJsonAsync("/staff/tables", new { number = 5 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<Venue> SeedVenue()
    {
        Venue venue = Venue.Create("Test Bar", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        await seed.SaveChangesAsync();

        return venue;
    }

    private HttpClient ClientFor(Venue venue, StaffRole role)
    {
        AccessToken token = _factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(Guid.CreateVersion7(), venue.Id, "someone", role);

        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        return client;
    }
}
