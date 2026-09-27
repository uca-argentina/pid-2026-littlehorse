using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Menu;

/// <summary>
/// US-07 against the real database: the administrator's switch, and what the
/// customer's menu says once it has been flipped.
/// </summary>
/// <remarks>
/// The handlers are exercised over a real DbContext and not a fake repository,
/// because the two things worth proving here only exist there: that the flag
/// survives a round trip to its column, and that the global query filter scopes
/// the lookup — an administrator of one venue must not be able to turn off a
/// drink of the one next door by knowing its id.
/// </remarks>
[Collection(nameof(SqlServerCollection))]
public sealed class ProductAvailabilityTests(SqlServerFixture sql)
{
    // Criterion 1, the half the administration screen owns.
    [Fact]
    public async Task MarkUnavailableAsync_WhenTheProductIsThisVenues_WritesTheSwitchOff()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues();

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Result<ProductSummary> result = await new MarkProductUnavailableHandler(new ProductRepository(asMine))
            .HandleAsync(gin.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using DrinkItDbContext fresh = sql.CreateContext(mine.Id);

        Assert.False((await fresh.Products.SingleAsync(product => product.Id == gin.Id)).IsAvailable);
    }

    // Criterion 2: reposition the bottle and it sells again, with nothing
    // reloaded by hand.
    [Fact]
    public async Task MarkAvailableAsync_WhenTheProductWasOff_WritesTheSwitchBackOn()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues();

        await using DrinkItDbContext off = sql.CreateContext(mine.Id);
        await new MarkProductUnavailableHandler(new ProductRepository(off)).HandleAsync(gin.Id, CancellationToken.None);

        await using DrinkItDbContext on = sql.CreateContext(mine.Id);
        await new MarkProductAvailableHandler(new ProductRepository(on)).HandleAsync(gin.Id, CancellationToken.None);

        await using DrinkItDbContext fresh = sql.CreateContext(mine.Id);

        Assert.True((await fresh.Products.SingleAsync(product => product.Id == gin.Id)).IsAvailable);
    }

    // The switch and the stock are two different questions (decided 2026-09-14).
    // Turning a product off must not quietly empty it, and turning it back on
    // must not restock it.
    [Fact]
    public async Task MarkUnavailableAsync_WhenSaved_LeavesTheStockAndTheSoftDeleteAlone()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues();

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        await new MarkProductUnavailableHandler(new ProductRepository(asMine)).HandleAsync(gin.Id, CancellationToken.None);

        await using DrinkItDbContext fresh = sql.CreateContext(mine.Id);
        Product stored = await fresh.Products.SingleAsync(product => product.Id == gin.Id);

        Assert.Equal(20, stored.Stock);
        Assert.True(stored.IsActive);
    }

    /// <summary>
    /// The most important one of this story. The id of another venue's product
    /// is a perfectly valid id and the endpoint takes it from the URL: the only
    /// thing standing between an administrator and the menu of the venue next
    /// door is the global query filter, so it is tested and not assumed.
    /// </summary>
    [Fact]
    public async Task MarkUnavailableAsync_WhenTheProductBelongsToAnotherVenue_FailsAndChangesNothing()
    {
        (Venue mine, Venue theirs, _, Product foreign) = await SeedTwoVenues();

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Result<ProductSummary> result = await new MarkProductUnavailableHandler(new ProductRepository(asMine))
            .HandleAsync(foreign.Id, CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);

        await using DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id);

        Assert.True((await asTheirs.Products.SingleAsync(product => product.Id == foreign.Id)).IsAvailable);
    }

    [Fact]
    public async Task MarkAvailableAsync_WhenTheProductBelongsToAnotherVenue_FailsAndChangesNothing()
    {
        (Venue mine, Venue theirs, _, Product foreign) = await SeedTwoVenues();

        await using DrinkItDbContext off = sql.CreateContext(theirs.Id);
        await new MarkProductUnavailableHandler(new ProductRepository(off)).HandleAsync(foreign.Id, CancellationToken.None);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        Result<ProductSummary> result = await new MarkProductAvailableHandler(new ProductRepository(asMine))
            .HandleAsync(foreign.Id, CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);

        await using DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id);

        Assert.False((await asTheirs.Products.SingleAsync(product => product.Id == foreign.Id)).IsAvailable);
    }

    /// <summary>
    /// Criterion 1 end to end through the database: the administrator flips the
    /// switch and the very query the customer's phone runs stops offering the
    /// drink — while still listing it, which is the whole point of the story.
    /// </summary>
    [Fact]
    public async Task ListForMenuAsync_AfterTheSwitchIsTurnedOff_StillShowsTheDrinkButNotAsOrderable()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues();

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        await new MarkProductUnavailableHandler(new ProductRepository(asMine)).HandleAsync(gin.Id, CancellationToken.None);

        await using DrinkItDbContext fresh = sql.CreateContext(mine.Id);
        MenuItem card = (await new ProductQueries(fresh).ListForMenuAsync(CancellationToken.None))
            .Single(item => item.Name == "Gin Tonic");

        Assert.False(card.IsOrderable);
    }

    // Criterion 2, the same way round: what the customer can order again.
    [Fact]
    public async Task ListForMenuAsync_AfterTheSwitchIsTurnedBackOn_OffersTheDrinkAgain()
    {
        (Venue mine, _, Product gin, _) = await SeedTwoVenues();

        await using DrinkItDbContext off = sql.CreateContext(mine.Id);
        await new MarkProductUnavailableHandler(new ProductRepository(off)).HandleAsync(gin.Id, CancellationToken.None);

        await using DrinkItDbContext on = sql.CreateContext(mine.Id);
        await new MarkProductAvailableHandler(new ProductRepository(on)).HandleAsync(gin.Id, CancellationToken.None);

        await using DrinkItDbContext fresh = sql.CreateContext(mine.Id);
        MenuItem card = (await new ProductQueries(fresh).ListForMenuAsync(CancellationToken.None))
            .Single(item => item.Name == "Gin Tonic");

        Assert.True(card.IsOrderable);
    }

    /// <summary>
    /// Running out locks the switch. Only stock can unlock it, and putting
    /// stock back is US-08: until then the drink stays off the menu, and the
    /// attempt leaves the row exactly as it was.
    /// </summary>
    [Fact]
    public async Task MarkAvailableAsync_WhenTheProductRanOut_IsRefusedAndWritesNothing()
    {
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(mine.Id);
        Product empty = Product.Create(mine.Id, "Gin Tonic", null, null, 4500m, 0, category.Id);

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.Add(mine);
        seed.Categories.Add(category);
        seed.Products.Add(empty);
        await seed.SaveChangesAsync();

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        MarkProductAvailableHandler handler = new(new ProductRepository(asMine));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.HandleAsync(empty.Id, CancellationToken.None));

        await using DrinkItDbContext fresh = sql.CreateContext(mine.Id);
        Product stored = await fresh.Products.SingleAsync(product => product.Id == empty.Id);

        Assert.Equal(0, stored.Stock);
        Assert.False(stored.IsOrderable);
    }

    private async Task<(Venue Mine, Venue Theirs, Product Gin, Product Foreign)> SeedTwoVenues()
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");
        Category mineCategory = SeedCategory.For(mine.Id);
        Category theirCategory = SeedCategory.For(theirs.Id);
        Product gin = Product.Create(mine.Id, "Gin Tonic", null, null, 4500m, 20, mineCategory.Id);
        Product foreign = Product.Create(theirs.Id, "Gin Tonic", null, null, 4500m, 20, theirCategory.Id);

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.Categories.AddRange(mineCategory, theirCategory);
        seed.Products.AddRange(gin, foreign);
        await seed.SaveChangesAsync();

        return (mine, theirs, gin, foreign);
    }
}
