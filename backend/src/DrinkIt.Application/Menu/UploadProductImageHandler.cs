using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>
/// The picture as it arrived. <paramref name="Length"/> is what the request
/// declared, checked before a byte is read; the content has to be seekable,
/// because the first bytes are read twice — once to tell the format, once to
/// store it.
/// </summary>
public sealed record UploadProductImageCommand(Guid ProductId, Stream Content, long Length);

public sealed record UploadedProductImage(string ImageUrl);

/// <summary>
/// Puts a picture on a product of the venue the signed-in administrator
/// belongs to. The product is looked up through the repository, which the
/// venue filter scopes, so another venue's product is simply not found.
/// </summary>
public sealed class UploadProductImageHandler(IProductRepository products, IImageStore images)
{
    /// <summary>Decided on 2026-09-15: a phone photo fits, a mistake does not.</summary>
    public const long MaxImageBytes = 5 * 1024 * 1024;

    public static readonly Error ImageTooLarge =
        new("product.image_too_large", "The picture cannot be larger than 5 MB.");

    public static readonly Error ImageFormatUnsupported =
        new("product.image_format_unsupported", "The picture has to be a JPEG, a PNG or a WebP.");

    public async Task<Result<UploadedProductImage>> HandleAsync(
        UploadProductImageCommand command,
        CancellationToken cancellationToken)
    {
        Product? product = await products.GetForUpdateAsync(command.ProductId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;
        if (command.Length > MaxImageBytes) return ImageTooLarge;

        ImageFormat? format = ImageFormat.Detect(await HeaderOf(command.Content, cancellationToken));

        if (format is null) return ImageFormatUnsupported;

        // A fresh name on every upload, never "products/{id}.png": the browser
        // and any cache in between would keep showing the old picture under
        // the same address after it was replaced.
        string name = $"products/{product.Id}/{Guid.CreateVersion7():N}.{format.Extension}";
        string imageUrl = await images.SaveAsync(name, command.Content, format.ContentType, cancellationToken);

        product.ReplaceImage(imageUrl);

        await products.SaveChangesAsync(cancellationToken);

        return new UploadedProductImage(product.ImageUrl!);
    }

    /// <summary>The first bytes, with the stream left where the store expects it: at the start.</summary>
    private static async Task<byte[]> HeaderOf(Stream content, CancellationToken cancellationToken)
    {
        byte[] header = new byte[ImageFormat.HeaderLength];
        int read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

        content.Seek(0, SeekOrigin.Begin);

        return header[..read];
    }
}
