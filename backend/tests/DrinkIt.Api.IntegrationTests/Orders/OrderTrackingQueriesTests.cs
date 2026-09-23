using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Orders;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Orders;

/// <summary>
/// US-12: what the customer's tracking screen reads, and who gets to read it.
/// </summary>
/// <remarks>
/// Criterion 5 lives here. A customer has no account, so the link is the only
/// proof an order is theirs, and this is where that proof is checked.
/// </remarks>
[Collection(nameof(SqlServerCollection))]
public sealed class OrderTrackingQueriesTests(SqlServerFixture sql)
{
    [Fact]
    public async Task FindAsync_WhenTheLinkIsRight_TellsTheCustomerWhereTheirOrderIs()
    {
        (Venue venue, Order order) = await AVenueWithAnOrderFor("María Quadro");

        TrackedOrder? found = await Tracking(venue)
            .FindAsync(order.Code.Value, order.TrackingToken.Value, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(order.Code.Value, found.Code);
        Assert.Equal("María Quadro", found.CustomerName);
        Assert.Equal(OrderStatus.Queued, found.Status);
        Assert.Equal(9000m, found.Total);
        Assert.Equal("Gin Tonic", found.Items.Single().ProductName);
        Assert.Equal(2, found.Items.Single().Quantity);
    }

    // Criterion 5: the codes run in order, so the one next door is this one
    // minus one. Knowing it has to get nobody anywhere.
    [Fact]
    public async Task FindAsync_WhenTheTokenIsWrong_FindsNothing()
    {
        (Venue venue, Order order) = await AVenueWithAnOrderFor("María Quadro");

        TrackedOrder? found = await Tracking(venue)
            .FindAsync(order.Code.Value, TrackingToken.New().Value, CancellationToken.None);

        Assert.Null(found);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-token")]
    [InlineData("9f3c2ba7")]
    public async Task FindAsync_WhenTheTokenIsNotEvenAToken_FindsNothing(string token)
    {
        (Venue venue, Order order) = await AVenueWithAnOrderFor("María Quadro");

        Assert.Null(await Tracking(venue).FindAsync(order.Code.Value, token, CancellationToken.None));
    }

    // The same code exists in every venue: they run per venue and both start at
    // A-0000. Somebody's link must not reach the order of another bar.
    [Fact]
    public async Task FindAsync_WhenTheOrderBelongsToAnotherVenue_FindsNothing()
    {
        (Venue mine, _) = await AVenueWithAnOrderFor("María Quadro");
        (_, Order theirs) = await AVenueWithAnOrderFor("Otra Persona");

        TrackedOrder? found = await Tracking(mine)
            .FindAsync(theirs.Code.Value, theirs.TrackingToken.Value, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindAsync_WhenNoOrderHasThatCode_FindsNothing()
    {
        (Venue venue, Order order) = await AVenueWithAnOrderFor("María Quadro");

        Assert.Null(await Tracking(venue)
            .FindAsync("Z-9999", order.TrackingToken.Value, CancellationToken.None));
    }

    /// <summary>
    /// A code that is not even a code answers the same as a code nobody has.
    /// </summary>
    /// <remarks>
    /// This arrives from the address bar, so it is whatever somebody pasted: a
    /// link a chat app cut in half, a phone keyboard that lowercased the letter,
    /// somebody working through codes by hand. None of that is a broken
    /// invariant — it is a link that leads nowhere, which is the one answer this
    /// endpoint gives. Reading it as a code first turned it into an exception,
    /// and the screen, which only knows 404, kept asking forever.
    /// </remarks>
    [Theory]
    [InlineData("k-4821")]
    [InlineData("K-482")]
    [InlineData("K4821")]
    [InlineData("K-48211")]
    [InlineData("1-4821")]
    [InlineData("")]
    [InlineData("undefined")]
    public async Task FindAsync_WhenTheCodeIsNotEvenACode_FindsNothing(string code)
    {
        (Venue venue, Order order) = await AVenueWithAnOrderFor("María Quadro");

        Assert.Null(await Tracking(venue)
            .FindAsync(code, order.TrackingToken.Value, CancellationToken.None));
    }

    /// <summary>
    /// She chose this on 2026-09-17: the link stops working once the drinks are
    /// handed over, so the one left in a shared phone's history stops being a
    /// way in.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Canceled)]
    public async Task FindAsync_WhenTheOrderIsOver_FindsNothing(OrderStatus over)
    {
        (Venue venue, Order order) = await AVenueWithAnOrderFor("María Quadro");

        await using (DrinkItDbContext moving = sql.CreateContext(venue.Id))
        {
            await moving.Orders
                .Where(row => row.Id == order.Id)
                .ExecuteUpdateAsync(row => row.SetProperty(o => o.Status, over));
        }

        Assert.Null(await Tracking(venue)
            .FindAsync(order.Code.Value, order.TrackingToken.Value, CancellationToken.None));
    }

    // Criterion 3 is a screen asking again every three seconds, so this query
    // runs a lot and must never hand the secret back out with the answer.
    [Fact]
    public async Task FindAsync_WhenItAnswers_NeverCarriesTheTokenBack()
    {
        (Venue venue, Order order) = await AVenueWithAnOrderFor("María Quadro");

        TrackedOrder found = (await Tracking(venue)
            .FindAsync(order.Code.Value, order.TrackingToken.Value, CancellationToken.None))!;

        Assert.DoesNotContain(
            nameof(TrackedOrder.Code) + "Token",
            string.Join(' ', typeof(TrackedOrder).GetProperties().Select(property => property.Name)),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", string.Join(' ', typeof(TrackedOrder).GetProperties().Select(p => p.Name)), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(found);
    }

    private OrderTrackingQueries Tracking(Venue venue) => new(sql.CreateContext(venue.Id));

    private async Task<(Venue Venue, Order Order)> AVenueWithAnOrderFor(string customer)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20);

        Order order = Order.Place(
            venue.Id,
            customer,
            OrderCode.First,
            [new NewOrderItem(gin.Id, "Gin Tonic", 4500m, 2, "sin hielo")]);

        order.Pay(DateTimeOffset.UtcNow);
        order.Enqueue();

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Products.Add(gin);
        seed.Orders.Add(order);
        seed.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await seed.SaveChangesAsync();

        return (venue, order);
    }
}
