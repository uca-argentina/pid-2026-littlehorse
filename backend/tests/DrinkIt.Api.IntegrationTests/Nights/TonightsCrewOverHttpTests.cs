using System.Net;
using System.Net.Http.Headers;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Nights;

/// <summary>
/// US-35, criterion 4, as the screens meet it: an account outside tonight's
/// crew signs in fine, and then the venue's orders answer it with a 403 of
/// its own type, so the screen can say "no estás en la noche de hoy" instead
/// of "tu cuenta no puede".
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class TonightsCrewOverHttpTests(SqlServerFixture sql) : IAsyncDisposable
{
    private const string NotTonight = "urn:drinkit:problem:auth:not-in-tonights-crew";

    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData(StaffRole.Kds, "/kds/queue")]
    [InlineData(StaffRole.Cashier, "/cashier/orders")]
    public async Task Get_WhenTheAccountWorksTonight_IsAnswered(StaffRole role, string path)
    {
        (Guid venue, Guid account) = await SeedTonight.AnAccountWorkingTonight(sql, role);

        using HttpResponseMessage response = await GetAs(venue, account, role, path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRole.Kds, "/kds/queue")]
    [InlineData(StaffRole.Cashier, "/cashier/orders")]
    public async Task Get_WhenTheAccountIsNotInTonightsCrew_IsForbiddenAndSaysWhy(StaffRole role, string path)
    {
        (Guid venue, _) = await SeedTonight.AnAccountWorkingTonight(sql, role);
        Guid outsider = await SeedTonight.AnAccountOffTonight(sql, venue, role);

        using HttpResponseMessage response = await GetAs(venue, outsider, role, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(NotTonight, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // A wrong role is still a wrong role, said as before: being in the night
    // does not make an administrator a KDS, nor the other way round.
    [Fact]
    public async Task Get_WhenTheRoleIsWrong_IsForbiddenAsBefore()
    {
        (Guid venue, Guid cashier) = await SeedTonight.AnAccountWorkingTonight(sql, StaffRole.Cashier);

        using HttpResponseMessage response = await GetAs(venue, cashier, StaffRole.Cashier, "/kds/queue");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("urn:drinkit:problem:auth:forbidden", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // The administrator is never limited by the night.
    [Fact]
    public async Task Get_WhenAnAdministratorListsTheNights_IsAnsweredWithoutWorkingOne()
    {
        (Guid venue, _) = await SeedTonight.AnAccountWorkingTonight(sql, StaffRole.Kds);

        using HttpResponseMessage response = await GetAs(venue, Guid.CreateVersion7(), StaffRole.Administrator, "/nights");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetAs(Guid venue, Guid account, StaffRole role, string path)
    {
        AccessToken token = _factory.Services.GetRequiredService<ITokenIssuer>().Issue(account, venue, "someone", role);

        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        return await client.GetAsync(path);
    }
}
