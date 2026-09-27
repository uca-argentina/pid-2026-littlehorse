using DrinkIt.Api.IntegrationTests.Persistence;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Venues;
using DrinkIt.Infrastructure.Menu;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Api.IntegrationTests.Menu;

/// <summary>
/// Editing a product while it is being sold.
/// </summary>
/// <remarks>
/// Uploading a photo reads the product, spends seconds sending bytes to blob
/// storage, and only then saves. A venue sells drinks the whole time that
/// takes. The administration screens and the till must not be able to knock
/// each other over.
/// </remarks>
[Collection(nameof(SqlServerCollection))]
public sealed class ProductWritesUnderSalesTests(SqlServerFixture sql)
{
    [Fact]
    public async Task SaveChangesAsync_WhenSomebodyBoughtOneWhileThePhotoWasUploading_StillSavesThePhoto()
    {
        (Venue venue, Product product) = await AVenueSelling(stock: 20);

        // The administrator's request: reads the product, and holds it while
        // the picture travels.
        await using DrinkItDbContext editing = sql.CreateContext(venue.Id);
        ProductRepository products = new(editing);
        Product being = (await products.GetForUpdateAsync(product.Id, CancellationToken.None))!;

        // A customer buys one in the meantime, on their own connection and the
        // way an order does it: one conditional statement.
        await using (DrinkItDbContext buying = sql.CreateContext(venue.Id))
        {
            await buying.Products
                .Where(row => row.Id == product.Id && row.Stock >= 1)
                .ExecuteUpdateAsync(row => row.SetProperty(p => p.Stock, p => p.Stock - 1));
        }

        being.ReplaceImage("https://images.example.com/gin.png");
        await products.SaveChangesAsync(CancellationToken.None);

        await using DrinkItDbContext check = sql.CreateContext(venue.Id);
        Product after = await check.Products.SingleAsync(row => row.Id == product.Id);

        // Both writes stand: they were never about the same thing.
        Assert.Equal("https://images.example.com/gin.png", after.ImageUrl);
        Assert.Equal(19, after.Stock);
    }

    private async Task<(Venue Venue, Product Product)> AVenueSelling(int stock)
    {
        Venue venue = Venue.Create("Bar de prueba", $"bar-{Guid.NewGuid():N}");
        Category category = SeedCategory.For(venue.Id);
        Product product = Product.Create(venue.Id, "Gin Tonic", null, null, 4500m, stock, category.Id);

        await using DrinkItDbContext seed = sql.CreateContext(venue.Id);
        seed.Venues.Add(venue);
        seed.Categories.Add(category);
        seed.Products.Add(product);
        await seed.SaveChangesAsync();

        return (venue, product);
    }
}
