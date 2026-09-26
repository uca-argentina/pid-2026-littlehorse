using DrinkIt.Application.Common;
using DrinkIt.Application.Menu;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Tests.Menu;

public class UploadProductImageHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static readonly byte[] APng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static readonly byte[] APdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A, 0x0A];

    private static Product AGinTonic() => Product.Create(TheVenue, "Gin Tonic", null, null, 4500m, 20, Guid.CreateVersion7());

    private static UploadProductImageCommand AnUploadFor(Product product, byte[] bytes) =>
        new(product.Id, new MemoryStream(bytes), bytes.Length);

    [Fact]
    public async Task HandleAsync_WhenTheImageIsValid_StoresItUnderTheProductAndKeepsTheAddress()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product);
        Fake.Images images = new();

        Result<UploadedProductImage> result = await HandlerOver(products, images)
            .HandleAsync(AnUploadFor(product, APng), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith($"products/{product.Id}/", images.SavedAs, StringComparison.Ordinal);
        Assert.EndsWith(".png", images.SavedAs, StringComparison.Ordinal);
        Assert.Equal("image/png", images.SavedContentType);
        Assert.Equal(APng, images.SavedBytes);
        Assert.Equal(Fake.Images.UrlFor(images.SavedAs!), product.ImageUrl);
        Assert.Equal(product.ImageUrl, result.Value.ImageUrl);
        Assert.True(products.Saved);
    }

    // A new name every time rather than "products/{id}.png": the browser and
    // any cache in between would keep showing the old picture under the same
    // address after it was replaced.
    [Fact]
    public async Task HandleAsync_WhenTheImageIsReplaced_StoresItUnderANewName()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product);
        Fake.Images images = new();
        UploadProductImageHandler handler = HandlerOver(products, images);

        await handler.HandleAsync(AnUploadFor(product, APng), CancellationToken.None);
        string? first = images.SavedAs;
        await handler.HandleAsync(AnUploadFor(product, APng), CancellationToken.None);

        Assert.NotEqual(first, images.SavedAs);
    }

    [Fact]
    public async Task HandleAsync_WhenTheProductDoesNotExistInThisVenue_FailsWithoutStoringAnything()
    {
        Fake.Products products = new(AGinTonic());
        Fake.Images images = new();

        Result<UploadedProductImage> result = await HandlerOver(products, images).HandleAsync(
            new UploadProductImageCommand(Guid.CreateVersion7(), new MemoryStream(APng), APng.Length),
            CancellationToken.None);

        Assert.Equal(ProductErrors.NotFound, result.Error);
        Assert.Null(images.SavedAs);
    }

    // Decided on 2026-09-15: five megabytes. A phone photo fits; a mistake does not.
    [Fact]
    public async Task HandleAsync_WhenTheImageIsLargerThanTheLimit_FailsWithoutStoringAnything()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product);
        Fake.Images images = new();

        Result<UploadedProductImage> result = await HandlerOver(products, images).HandleAsync(
            new UploadProductImageCommand(product.Id, new MemoryStream(APng), UploadProductImageHandler.MaxImageBytes + 1),
            CancellationToken.None);

        Assert.Equal(UploadProductImageHandler.ImageTooLarge, result.Error);
        Assert.Null(images.SavedAs);
        Assert.Null(product.ImageUrl);
    }

    [Fact]
    public async Task HandleAsync_WhenTheBytesAreNotAnImageWeShow_FailsWithoutStoringAnything()
    {
        Product product = AGinTonic();
        Fake.Products products = new(product);
        Fake.Images images = new();

        Result<UploadedProductImage> result = await HandlerOver(products, images)
            .HandleAsync(AnUploadFor(product, APdf), CancellationToken.None);

        Assert.Equal(UploadProductImageHandler.ImageFormatUnsupported, result.Error);
        Assert.Null(images.SavedAs);
        Assert.Null(product.ImageUrl);
    }

    private static UploadProductImageHandler HandlerOver(Fake.Products products, Fake.Images images) =>
        new(products, images);

    private static class Fake
    {
        public sealed class Products(Product stored) : IProductRepository
        {
            public Task<bool> SaveStockAdjustmentAsync(Product product, int change, CancellationToken cancellationToken) =>
                Task.FromResult(true);

            public bool Saved { get; private set; }

            public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
                Task.FromResult(false);

            public Task AddAsync(Product product, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(stored.Id == id ? stored : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken)
            {
                Saved = true;

                return Task.CompletedTask;
            }
        }

        public sealed class Images : IImageStore
        {
            public string? SavedAs { get; private set; }

            public string? SavedContentType { get; private set; }

            public byte[]? SavedBytes { get; private set; }

            public static string UrlFor(string name) => $"https://images.example.com/{name}";

            public async Task<string> SaveAsync(string name, Stream content, string contentType, CancellationToken cancellationToken)
            {
                using MemoryStream copy = new();
                await content.CopyToAsync(copy, cancellationToken);

                SavedAs = name;
                SavedContentType = contentType;
                SavedBytes = copy.ToArray();

                return UrlFor(name);
            }
        }
    }
}
