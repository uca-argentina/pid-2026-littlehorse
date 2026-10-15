using System.Net;
using System.Net.Http.Headers;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Waiter;

/// <summary>
/// The waiter's endpoints as the phone reaches them: through the router, the
/// token and the night's crew. What a unit test of the endpoint cannot show is
/// who gets in.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class WaiterOverHttpTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task List_WhenThereIsNoToken_IsUnauthorized()
    {
        using HttpClient anonymous = _factory.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync("/waiter/tables");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The other roles have their own screens, and none of them is this one:
    // not even the administrator reads the waiter's list by omission.
    [Theory]
    [InlineData(StaffRole.Administrator)]
    [InlineData(StaffRole.Kds)]
    [InlineData(StaffRole.Cashier)]
    public async Task List_WhenTheTokenIsNotAWaiters_IsForbidden(StaffRole role)
    {
        Venue venue = await SeedVenue();
        using HttpClient staff = ClientFor(venue, role);

        using HttpResponseMessage response = await staff.GetAsync("/waiter/tables");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // US-35, criterion 4: the right role is not enough while the account is
    // not in the crew of the night. Here no night lists it.
    [Fact]
    public async Task List_WhenTheWaiterIsNotInTonightsCrew_IsForbidden()
    {
        Venue venue = await SeedVenue();
        using HttpClient waiter = ClientFor(venue, StaffRole.Waiter);

        using HttpResponseMessage response = await waiter.GetAsync("/waiter/tables");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Take_WhenThereIsNoToken_IsUnauthorized()
    {
        using HttpClient anonymous = _factory.CreateClient();

        using HttpResponseMessage response = await anonymous.PostAsync($"/waiter/tables/{Guid.CreateVersion7()}/take", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Venue> SeedVenue()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");

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
