using System.Net;
using DrinkIt.Infrastructure.Menu;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;

namespace DrinkIt.Api.IntegrationTests.Menu;

/// <summary>
/// Against the real storage API, emulated: what matters is that the address
/// handed back is one a browser fetches anonymously, with the right content
/// type. A store that returns an address only the SDK can read would put the
/// menu's pictures behind a login.
/// </summary>
public sealed class AzureBlobImageStoreTests : IAsyncLifetime
{
    private static readonly byte[] APng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    // Same image and flag as docker-compose.yml. The SDK speaks a newer API
    // version than the emulator admits to; without the flag it refuses every call.
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public Task InitializeAsync() => _azurite.StartAsync();

    public Task DisposeAsync() => _azurite.DisposeAsync().AsTask();

    [Fact]
    public async Task SaveAsync_WhenCalled_ReturnsAnAddressABrowserFetchesWithTheContentType()
    {
        AzureBlobImageStore store = StoreOver("product-images");

        string url = await store.SaveAsync("products/1/a.png", new MemoryStream(APng), "image/png", CancellationToken.None);

        using HttpClient browser = new();
        using HttpResponseMessage fetched = await browser.GetAsync(new Uri(url));

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(APng, await fetched.Content.ReadAsByteArrayAsync());
    }

    // The container is created on first use, public for reading: nothing has to
    // be provisioned by hand before the first photo, in Azure or on a laptop.
    [Fact]
    public async Task SaveAsync_WhenTheContainerDoesNotExistYet_CreatesIt()
    {
        AzureBlobImageStore store = StoreOver($"fresh-{Guid.NewGuid():N}");

        string url = await store.SaveAsync("products/1/a.png", new MemoryStream(APng), "image/png", CancellationToken.None);

        using HttpClient browser = new();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri(url))).StatusCode);
    }

    [Fact]
    public async Task SaveAsync_WhenTheNameIsReused_ReplacesTheContent()
    {
        AzureBlobImageStore store = StoreOver("product-images");
        byte[] other = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

        await store.SaveAsync("products/1/b.png", new MemoryStream(APng), "image/png", CancellationToken.None);
        string url = await store.SaveAsync("products/1/b.png", new MemoryStream(other), "image/jpeg", CancellationToken.None);

        using HttpClient browser = new();
        using HttpResponseMessage fetched = await browser.GetAsync(new Uri(url));

        Assert.Equal(other, await fetched.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/jpeg", fetched.Content.Headers.ContentType?.MediaType);
    }

    private AzureBlobImageStore StoreOver(string container) =>
        new(Options.Create(new ImageStorageOptions
        {
            ConnectionString = _azurite.GetConnectionString(),
            ContainerName = container,
        }));
}
