using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Menu;

/// <summary>
/// A product name is taken inside one venue and free in every other one. The
/// handler asks before inserting, but that check is only worth as much as the
/// index behind it, and both have to agree on which venue they are talking about.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class ProductRepositoryTests(SqlServerFixture sql)
{
    [Fact]
    public async Task NameExistsAsync_WhenThisVenueSellsIt_IsTrue()
    {
        (Venue mine, _) = await SeedTwoVenues("Gin Tonic", "Gin Tonic");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.True(await new ProductRepository(asMine).NameExistsAsync("Gin Tonic", CancellationToken.None));
    }

    // The contract of NameExistsAsync: casing does not make a different product.
    // The handler trims but does not lowercase — the name is displayed as typed —
    // so it is the database that has to ignore case here.
    [Fact]
    public async Task NameExistsAsync_WhenTypedWithOtherCasing_IsStillTrue()
    {
        (Venue mine, _) = await SeedTwoVenues("Gin Tonic", "Fernet");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.True(await new ProductRepository(asMine).NameExistsAsync("GIN TONIC", CancellationToken.None));
    }

    // The row exists in the table, and this venue must not see it: otherwise
    // one venue selling a "Gin Tonic" would block every other venue.
    [Fact]
    public async Task NameExistsAsync_WhenOnlyAnotherVenueSellsIt_IsFalse()
    {
        (Venue mine, _) = await SeedTwoVenues("Fernet", "Gin Tonic");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.False(await new ProductRepository(asMine).NameExistsAsync("Gin Tonic", CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenAnotherVenueAlreadySellsThatName_StillCreatesTheProduct()
    {
        (Venue mine, _) = await SeedTwoVenues("Fernet", "Gin Tonic");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        ProductRepository repository = new(asMine);

        await repository.AddAsync(AProduct(mine.Id, "Gin Tonic"), CancellationToken.None);

        Assert.True(await repository.NameExistsAsync("Gin Tonic", CancellationToken.None));
    }

    /// <summary>
    /// The index is the last line of defence. Two administrators saving the same
    /// new product at once both pass the availability check, and only the
    /// database can stop the second one.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenThisVenueAlreadySellsThatName_IsRefusedByTheDatabase()
    {
        (Venue mine, _) = await SeedTwoVenues("Gin Tonic", "Fernet");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        await Assert.ThrowsAsync<DbUpdateException>(() => new ProductRepository(asMine).AddAsync(
            AProduct(mine.Id, "Gin Tonic"),
            CancellationToken.None));
    }

    // Every column round-trips, the optional ones as null and the price with
    // its cents: a price stored as an integer would silently round.
    [Fact]
    public async Task AddAsync_WhenSaved_ReadsBackEveryField()
    {
        (Venue mine, _) = await SeedTwoVenues("Fernet", "Fernet");
        Product product = Product.Create(mine.Id, "Gin Tonic", null, null, 4500.50m, 0, ProductCategory.Drink);

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        await new ProductRepository(asMine).AddAsync(product, CancellationToken.None);

        await using DrinkItDbContext fresh = sql.CreateContext(mine.Id);
        Product stored = await fresh.Products.SingleAsync(p => p.Id == product.Id);

        Assert.Equal("Gin Tonic", stored.Name);
        Assert.Null(stored.Description);
        Assert.Null(stored.ImageUrl);
        Assert.Equal(4500.50m, stored.Price);
        Assert.Equal(0, stored.Stock);
        Assert.Equal(ProductCategory.Drink, stored.Category);
        Assert.True(stored.IsAvailable);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task ListAsync_WhenAnotherVenueHasProducts_LeavesThemOut()
    {
        (Venue mine, _) = await SeedTwoVenues("Fernet", "Gin Tonic");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        IReadOnlyList<ProductListItem> listed = await new ProductQueries(asMine).ListAsync(CancellationToken.None);

        Assert.Equal(["Fernet"], listed.Select(product => product.Name));
    }

    // Alphabetical, so the administrator finds a product the way they would on
    // a printed menu. And a product with no stock left says so: the listing
    // shows "sin stock" without the screen having to know the rule.
    [Fact]
    public async Task ListAsync_WhenTheVenueHasSeveralProducts_ReturnsThemByNameWithTheirStockState()
    {
        (Venue mine, _) = await SeedTwoVenues("Gin Tonic", "Nothing");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Products.Add(Product.Create(mine.Id, "Aperol Spritz", "Aperol, prosecco, soda", null, 5200m, 0, ProductCategory.Drink));
        await seed.SaveChangesAsync();

        IReadOnlyList<ProductListItem> listed = await new ProductQueries(seed).ListAsync(CancellationToken.None);

        Assert.Equal(["Aperol Spritz", "Gin Tonic"], listed.Select(product => product.Name));
        Assert.True(listed[0].IsSoldOut);
        Assert.Equal("Aperol, prosecco, soda", listed[0].Description);
        Assert.Equal(5200m, listed[0].Price);
        Assert.False(listed[1].IsSoldOut);
    }

    // The id of another venue's product is a valid id: it is the filter, not
    // the lookup, that has to say "not yours".
    [Fact]
    public async Task GetForUpdateAsync_WhenTheProductBelongsToAnotherVenue_FindsNothing()
    {
        (Venue mine, Venue theirs) = await SeedTwoVenues("Fernet", "Gin Tonic");

        await using DrinkItDbContext asTheirs = sql.CreateContext(theirs.Id);
        Product foreign = await asTheirs.Products.SingleAsync(product => product.Name == "Gin Tonic");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);

        Assert.Null(await new ProductRepository(asMine).GetForUpdateAsync(foreign.Id, CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheImageWasReplaced_WritesTheNewAddress()
    {
        (Venue mine, _) = await SeedTwoVenues("Fernet", "Gin Tonic");

        await using DrinkItDbContext asMine = sql.CreateContext(mine.Id);
        ProductRepository repository = new(asMine);
        Product fernet = (await asMine.Products.SingleAsync(product => product.Name == "Fernet"));
        Product? tracked = await repository.GetForUpdateAsync(fernet.Id, CancellationToken.None);
        tracked!.ReplaceImage("https://images.example.com/products/fernet/new.png");
        await repository.SaveChangesAsync(CancellationToken.None);

        await using DrinkItDbContext fresh = sql.CreateContext(mine.Id);
        Product stored = await fresh.Products.SingleAsync(product => product.Id == fernet.Id);

        Assert.Equal("https://images.example.com/products/fernet/new.png", stored.ImageUrl);
    }

    private static Product AProduct(Guid venueId, string name) =>
        Product.Create(venueId, name, "Something to drink.", "https://images.example.com/drink.jpg", 4500m, 20, ProductCategory.Drink);

    private async Task<(Venue Mine, Venue Theirs)> SeedTwoVenues(string mineProduct, string theirsProduct)
    {
        // Slugs are unique platform-wide, so every test needs its own.
        Venue mine = Venue.Create("Bar Mine", $"bar-{Guid.NewGuid():N}");
        Venue theirs = Venue.Create("Bar Theirs", $"bar-{Guid.NewGuid():N}");

        await using DrinkItDbContext seed = sql.CreateContext(mine.Id);
        seed.Venues.AddRange(mine, theirs);
        seed.Products.AddRange(AProduct(mine.Id, mineProduct), AProduct(theirs.Id, theirsProduct));
        await seed.SaveChangesAsync();

        return (mine, theirs);
    }
}
