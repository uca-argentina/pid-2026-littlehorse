using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Staff;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;
using DrinkIt.Infrastructure.Staff;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Persistence;

/// <summary>
/// US-30, the reading half: what the administration listings hand to the
/// screen, including the row that was there before the columns were.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class AuditReadTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Evening = new(2026, 9, 27, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_WhenAProductWasCreatedAndThenEdited_BringsBothMarks()
    {
        Venue venue = await SeedVenue();
        Guid productId = await SeedProductThenEditIt(venue);

        await using DrinkItDbContext read = sql.CreateContext(venue.Id);
        ProductListItem listed = (await new ProductQueries(read).ListAsync(CancellationToken.None)).Single(row => row.Id == productId);

        Assert.Equal(new AuditInfo(Evening, "euge", Evening.AddHours(2), "pablo"), listed.Audit);
    }

    // Criterion 4: a row from before the columns existed says nothing, and the
    // read must not turn that into a date.
    [Fact]
    public async Task ListAsync_WhenAProductPredatesTheAudit_BringsNothingInsteadOfADate()
    {
        Venue venue = await SeedVenue();

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        Category category = SeedCategory.For(seed, venue.Id);
        seed.Products.Add(Product.Create(venue.Id, "Vieja", null, null, 1000m, 5, category.Id));
        await seed.SaveChangesAsync();

        ProductListItem listed = (await new ProductQueries(seed).ListAsync(CancellationToken.None)).Single();

        Assert.Equal(new AuditInfo(null, null, null, null), listed.Audit);
    }

    [Fact]
    public async Task ListAsync_WhenAStaffUserWasCreatedAndThenChanged_BringsBothMarks()
    {
        Venue venue = await SeedVenue();
        StaffUser member = StaffUser.Create(venue.Id, "martin.p", "hash-of-a-long-enough-password", StaffRole.Waiter);

        await using (DrinkItDbContext creating = ContextFor(venue, "euge", Evening))
        {
            creating.StaffUsers.Add(member);
            await creating.SaveChangesAsync();
        }

        await using (DrinkItDbContext changing = ContextFor(venue, "pablo", Evening.AddHours(1)))
        {
            (await changing.StaffUsers.SingleAsync(row => row.Id == member.Id)).ChangeRole(StaffRole.Kds);
            await changing.SaveChangesAsync();
        }

        await using DrinkItDbContext read = sql.CreateContext(venue.Id);
        StaffUserListItem listed = (await new StaffUserQueries(read).ListAsync(CancellationToken.None)).Single();

        Assert.Equal(new AuditInfo(Evening, "euge", Evening.AddHours(1), "pablo"), listed.Audit);
    }

    private DrinkItDbContext ContextFor(Venue venue, string username, DateTimeOffset now) =>
        sql.CreateContext(venue.Id, new AuditInterceptor(new FixedClock(now), new FixedUser(username)));

    private async Task<Guid> SeedProductThenEditIt(Venue venue)
    {
        Guid id;

        await using (DrinkItDbContext creating = ContextFor(venue, "euge", Evening))
        {
            Category category = SeedCategory.For(creating, venue.Id);
            Product product = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, 20, category.Id);
            creating.Products.Add(product);
            await creating.SaveChangesAsync();
            id = product.Id;
        }

        await using DrinkItDbContext editing = ContextFor(venue, "pablo", Evening.AddHours(2));
        Product tracked = await editing.Products.SingleAsync(row => row.Id == id);
        tracked.Update("Gin Tonic", null, 5200m, tracked.CategoryId);
        await editing.SaveChangesAsync();

        return id;
    }

    private async Task<Venue> SeedVenue()
    {
        Venue venue = Venue.Create("Bar Audit", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        await seed.SaveChangesAsync();

        return venue;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedUser(string username) : ICurrentStaffUser
    {
        public string? Username => username;
    }
}
