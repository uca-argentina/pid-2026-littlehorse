using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Staff;

/// <summary>
/// The role travels as its name, and only as one of the names we hand out.
/// Proved over HTTP and not by calling the endpoint: the JSON reader is what
/// decides, and "2" or "Administrator,Kds" must never reach a use case as a
/// role — an enum parser would read the second as 1|2, which is Waiter.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class StaffRoleOverHttpTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CreateStaffUser_WhenTheRoleIsOneOfOurNames_CreatesIt()
    {
        using HttpResponseMessage response = await CreateWithRole("\"Kds\"");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"role\":\"Kds\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // Written lowercase by a phone keyboard, or by hand. The role picker sends
    // a fixed value, but nothing on the wire guarantees it.
    [Fact]
    public async Task CreateStaffUser_WhenTheRoleIsTypedWithOtherCasing_StillCreatesIt()
    {
        using HttpResponseMessage response = await CreateWithRole("\"kds\"");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"role\":\"Kds\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("\"Waiter, Administrator\"")]
    [InlineData("\"2\"")]
    [InlineData("2")]
    [InlineData("\"99\"")]
    [InlineData("\"Chef\"")]
    [InlineData("\"Administrator,Kds\"")]
    public async Task CreateStaffUser_WhenTheRoleIsNotOneOfOurNames_RejectsIt(string role)
    {
        using HttpResponseMessage response = await CreateWithRole(role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // An enum left out of a body arrives as its first value, and the first
    // role is Administrator: a request without a role must not hire one.
    [Fact]
    public async Task CreateStaffUser_WhenTheRoleIsMissing_RejectsIt()
    {
        using HttpResponseMessage response = await CreateWithRole(roleJson: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> CreateWithRole(string? roleJson)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");

        await using (DrinkItDbContext seed = sql.CreateContext(venue.Id))
        {
            seed.Venues.Add(venue);
            await seed.SaveChangesAsync();
        }

        AccessToken token = _factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(Guid.CreateVersion7(), venue.Id, "admin", StaffRole.Administrator);

        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        string role = roleJson is null ? string.Empty : $",\"role\":{roleJson}";
        string body = $$"""{"username":"martin{{Random.Shared.Next(1000, 9999)}}","password":"a long enough password"{{role}}}""";

        return await client.PostAsync("/staff/users", new StringContent(body, Encoding.UTF8, "application/json"));
    }
}
