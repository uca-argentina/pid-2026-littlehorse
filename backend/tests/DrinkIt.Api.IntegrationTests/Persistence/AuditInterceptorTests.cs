using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Persistence;

/// <summary>
/// US-30: who touched what and when, stamped in the one place every save goes
/// through, against a real SQL Server so the columns are the ones that ship.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class AuditInterceptorTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Evening = new(2026, 9, 27, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SaveChanges_WhenSomethingIsAdded_StampsWhenAndByWhomItWasCreated()
    {
        Venue venue = await SeedVenue();

        await using DrinkItDbContext context = ContextFor(venue, "euge", Evening);
        Category category = SeedCategory.For(context, venue.Id);
        Product product = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        Product stored = await Stored(venue, product.Id);

        Assert.Equal(Evening, stored.CreatedAt);
        Assert.Equal("euge", stored.CreatedBy);
        Assert.Null(stored.LastModifiedAt);
        Assert.Null(stored.LastModifiedBy);
    }

    [Fact]
    public async Task SaveChanges_WhenSomethingIsChanged_StampsWhenAndByWhomAndLeavesTheCreationAlone()
    {
        Venue venue = await SeedVenue();
        Product product = await SeedProduct(venue, "euge", Evening);

        await using DrinkItDbContext context = ContextFor(venue, "pablo", Evening.AddHours(2));
        Product tracked = await context.Products.SingleAsync(row => row.Id == product.Id);
        tracked.Update("Gin Tonic", null, 5200m, tracked.CategoryId);
        await context.SaveChangesAsync();

        Product stored = await Stored(venue, product.Id);

        Assert.Equal(Evening.AddHours(2), stored.LastModifiedAt);
        Assert.Equal("pablo", stored.LastModifiedBy);
        Assert.Equal(Evening, stored.CreatedAt);
        Assert.Equal("euge", stored.CreatedBy);
    }

    // A save with nothing to write is not a modification: otherwise opening a
    // screen that happens to save would move the mark.
    [Fact]
    public async Task SaveChanges_WhenNothingChanged_LeavesTheMarksAsTheyWere()
    {
        Venue venue = await SeedVenue();
        Product product = await SeedProduct(venue, "euge", Evening);

        await using DrinkItDbContext context = ContextFor(venue, "pablo", Evening.AddHours(2));
        await context.Products.SingleAsync(row => row.Id == product.Id);
        await context.SaveChangesAsync();

        Product stored = await Stored(venue, product.Id);

        Assert.Null(stored.LastModifiedAt);
        Assert.Null(stored.LastModifiedBy);
    }

    // Somebody who is not signed in is no user, and no user is invented: the
    // moment is still worth having.
    [Fact]
    public async Task SaveChanges_WhenNobodyIsSignedIn_StampsTheMomentAndNoAuthor()
    {
        Venue venue = await SeedVenue();

        Product product = await SeedProduct(venue, username: null, Evening);
        Product stored = await Stored(venue, product.Id);

        Assert.Equal(Evening, stored.CreatedAt);
        Assert.Null(stored.CreatedBy);
    }

    // The venue and the order are written by the system and by customers, so
    // they carry when and never who.
    [Fact]
    public async Task SaveChanges_WhenAVenueAndAnOrderAreAdded_StampsWhenTheyWereCreated()
    {
        Venue venue = Venue.Create("Bar Audit", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product gin = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);
        Order order = Order.Place(venue.Id, "Maria", OrderCode.First, [new NewOrderItem(gin.Id, "Gin Tonic", 4500m, 1, null)]);

        await using DrinkItDbContext context = ContextFor(venue, "euge", Evening);
        context.Venues.Add(venue);
        context.Categories.Add(category);
        context.Products.Add(gin);
        context.Orders.Add(order);
        context.Entry(order).Property("IdempotencyKey").CurrentValue = Guid.NewGuid().ToString();
        await context.SaveChangesAsync();

        await using DrinkItDbContext fresh = sql.CreateContext(venue.Id);

        Assert.Equal(Evening, (await fresh.Venues.SingleAsync(row => row.Id == venue.Id)).CreatedAt);
        Assert.Equal(Evening, (await fresh.Orders.SingleAsync(row => row.Id == order.Id)).CreatedAt);
    }

    [Fact]
    public async Task SaveChanges_WhenAStaffUserIsAddedAndThenDeactivated_StampsBothMoments()
    {
        Venue venue = await SeedVenue();
        StaffUser member = StaffUser.Create(venue.Id, "martin.p", "hash-of-a-long-enough-password", StaffRole.Waiter);

        await using (DrinkItDbContext creating = ContextFor(venue, "euge", Evening))
        {
            creating.StaffUsers.Add(member);
            await creating.SaveChangesAsync();
        }

        await using (DrinkItDbContext deactivating = ContextFor(venue, "pablo", Evening.AddDays(1)))
        {
            StaffUser tracked = await deactivating.StaffUsers.SingleAsync(row => row.Id == member.Id);
            tracked.Deactivate();
            await deactivating.SaveChangesAsync();
        }

        await using DrinkItDbContext fresh = sql.CreateContext(venue.Id);
        StaffUser stored = await fresh.StaffUsers.SingleAsync(row => row.Id == member.Id);

        Assert.Equal("euge", stored.CreatedBy);
        Assert.Equal(Evening, stored.CreatedAt);
        Assert.Equal("pablo", stored.LastModifiedBy);
        Assert.Equal(Evening.AddDays(1), stored.LastModifiedAt);
    }

    // Categories were already ordered by their own creation time, which the
    // handler decides: the stamp fills what is empty and never overwrites it.
    [Fact]
    public async Task SaveChanges_WhenTheCreationTimeWasAlreadySet_KeepsIt()
    {
        Venue venue = await SeedVenue();
        Category early = Category.Create(venue.Id, "Vinos", Evening.AddDays(-30));

        await using DrinkItDbContext context = ContextFor(venue, "euge", Evening);
        context.Categories.Add(early);
        await context.SaveChangesAsync();

        await using DrinkItDbContext fresh = sql.CreateContext(venue.Id);
        Category stored = await fresh.Categories.SingleAsync(row => row.Id == early.Id);

        Assert.Equal(Evening.AddDays(-30), stored.CreatedAt);
        Assert.Equal("euge", stored.CreatedBy);
    }

    // The sale takes stock with a statement of its own, not through SaveChanges:
    // a drink being sold is not somebody editing the product, so the mark stays.
    [Fact]
    public async Task SaveStockAdjustmentAsync_WhenTheStockMoves_DoesNotCountAsAnEdit()
    {
        Venue venue = await SeedVenue();
        Product product = await SeedProduct(venue, "euge", Evening);

        await using DrinkItDbContext context = ContextFor(venue, "pablo", Evening.AddHours(3));
        Product tracked = await context.Products.SingleAsync(row => row.Id == product.Id);
        await new ProductRepository(context).SaveStockAdjustmentAsync(tracked, -1, CancellationToken.None);

        Product stored = await Stored(venue, product.Id);

        Assert.Equal(19, stored.Stock);
        Assert.Null(stored.LastModifiedAt);
    }

    private DrinkItDbContext ContextFor(Venue venue, string? username, DateTimeOffset now) =>
        sql.CreateContext(venue.Id, new AuditInterceptor(new FixedClock(now), new FixedUser(username)));

    private async Task<Venue> SeedVenue()
    {
        Venue venue = Venue.Create("Bar Audit", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        await seed.SaveChangesAsync();

        return venue;
    }

    private async Task<Product> SeedProduct(Venue venue, string? username, DateTimeOffset now)
    {
        await using DrinkItDbContext context = ContextFor(venue, username, now);
        Category category = SeedCategory.For(context, venue.Id);
        Product product = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product;
    }

    private async Task<Product> Stored(Venue venue, Guid id)
    {
        await using DrinkItDbContext fresh = sql.CreateContext(venue.Id);

        return await fresh.Products.SingleAsync(row => row.Id == id);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedUser(string? username) : ICurrentStaffUser
    {
        public string? Username => username;
    }
}
