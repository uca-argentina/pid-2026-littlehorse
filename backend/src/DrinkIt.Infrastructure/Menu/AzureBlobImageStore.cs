using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DrinkIt.Application.Menu;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Menu;

/// <summary>
/// The pictures as blobs, in one container that anyone can read: the address
/// handed back goes straight into an img src on the customer's phone, with no
/// token in between.
/// </summary>
internal sealed class AzureBlobImageStore(IOptions<ImageStorageOptions> options) : IImageStore
{
    private readonly BlobContainerClient _container =
        new(options.Value.ConnectionString, options.Value.ContainerName);

    public async Task<string> SaveAsync(string name, Stream content, string contentType, CancellationToken cancellationToken)
    {
        // On every save rather than once at start-up: a request when the
        // container already exists costs nothing worth a race to avoid, and
        // nothing has to be provisioned by hand before the first photo.
        await _container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

        BlobClient blob = _container.GetBlobClient(name);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);

        return blob.Uri.ToString();
    }
}
