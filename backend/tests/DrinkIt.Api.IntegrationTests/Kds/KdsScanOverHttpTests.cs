using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DrinkIt.Api.IntegrationTests.Common;
using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Authentication;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DrinkIt.Api.IntegrationTests.Kds;

/// <summary>
/// US-20 over HTTP: the bar's tablet sends what its camera or reader read, and
/// the venue it scans in comes from its token, never from the request.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class KdsScanOverHttpTests(SqlServerFixture sql) : IAsyncDisposable
{
    private readonly DrinkItApiFactory _factory = new(sql.ConnectionString);

    /// <summary>Each venue's bar, in tonight's crew: the only account that may scan there.</summary>
    private readonly Dictionary<Guid, Guid> _barOf = [];

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Scan_WhenTheOrderIsReady_RespondsWithWhoseItWasAndDeliversIt()
    {
        Venue venue = await ASeededVenue();
        Order order = await AReadyOrder(venue);

        using HttpResponseMessage response = await ScanAs(venue, StaffRole.Kds, $$"""{"read":"{{order.TrackingToken.Value}}"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(order.Code.Value, body.RootElement.GetProperty("code").GetString());
        Assert.Equal("María Quadro", body.RootElement.GetProperty("customerName").GetString());
        Assert.Equal("Delivered", body.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(OrderStatus.Delivered, await StatusOf(venue, order));
    }

    // US-19, criterion 4: said, not refused — the screen needs whose order it was.
    [Fact]
    public async Task Scan_WhenTheOrderWasAlreadyDelivered_RespondsAlreadyDelivered()
    {
        Venue venue = await ASeededVenue();
        Order order = await AReadyOrder(venue);
        string read = $$"""{"read":"{{order.TrackingToken.Value}}"}""";
        (await ScanAs(venue, StaffRole.Kds, read)).Dispose();

        using HttpResponseMessage response = await ScanAs(venue, StaffRole.Kds, read);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"outcome\":\"AlreadyDelivered\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // The station's venue is in its token. Another venue's QR is a stranger's.
    [Fact]
    public async Task Scan_WhenTheOrderBelongsToAnotherVenue_RespondsNotFoundAndChangesNothing()
    {
        Venue mine = await ASeededVenue();
        Venue theirs = await ASeededVenue();
        Order order = await AReadyOrder(theirs);

        using HttpResponseMessage response = await ScanAs(mine, StaffRole.Kds, $$"""{"read":"{{order.TrackingToken.Value}}"}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("urn:drinkit:problem:kds:unknown-code", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(OrderStatus.Ready, await StatusOf(theirs, order));
    }

    // Nothing read is nothing found, not a crash.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"read":null}""")]
    public async Task Scan_WhenNothingWasRead_RespondsNotFound(string body)
    {
        Venue venue = await ASeededVenue();

        using HttpResponseMessage response = await ScanAs(venue, StaffRole.Kds, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scan_AsAnAdministrator_IsForbidden()
    {
        Venue venue = await ASeededVenue();
        Order order = await AReadyOrder(venue);

        using HttpResponseMessage response = await ScanAs(venue, StaffRole.Administrator, $$"""{"read":"{{order.TrackingToken.Value}}"}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OrderStatus.Ready, await StatusOf(venue, order));
    }

    private async Task<HttpResponseMessage> ScanAs(Venue venue, StaffRole role, string body)
    {
        Guid account = role == StaffRole.Kds ? _barOf[venue.Id] : Guid.CreateVersion7();
        AccessToken token = _factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(account, venue.Id, "barra.demo", role);

        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        return await client.PostAsync("/kds/scan", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private async Task<OrderStatus> StatusOf(Venue venue, Order order)
    {
        await using DrinkItDbContext check = sql.CreateContext(venue.Id);

        return (await check.Orders.SingleAsync(row => row.Id == order.Id)).Status;
    }

    private async Task<Venue> ASeededVenue()
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        await seed.SaveChangesAsync();

        _barOf[venue.Id] = await SeedTonight.AnAccountWorkingTonight(sql, venue.Id, StaffRole.Kds);

        return venue;
    }

    private async Task<Order> AReadyOrder(Venue venue)
    {
        Order order = Order.Place(
            venue.Id,
            Guid.CreateVersion7(),
            "María Quadro",
            OrderCode.Parse($"K-{Random.Shared.Next(1000, 9999)}"),
            [new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)]);
        order.Pay(DateTimeOffset.UtcNow.AddMinutes(-10), PaymentMethod.Digital);
        order.Enqueue();
        order.StartPreparing();
        order.MarkReady();
        order.ClearDomainEvents();

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return order;
    }
}
