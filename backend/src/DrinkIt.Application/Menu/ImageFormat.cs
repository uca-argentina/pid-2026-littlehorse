namespace DrinkIt.Application.Menu;

/// <summary>
/// One of the picture formats the menu shows, recognised by the first bytes of
/// the file. The file name and the content type a browser sends are whatever
/// the client says they are; the bytes are not.
/// </summary>
public sealed record ImageFormat(string ContentType, string Extension)
{
    /// <summary>Enough bytes to tell the three formats apart; WebP needs twelve.</summary>
    public const int HeaderLength = 12;

    public static readonly ImageFormat Jpeg = new("image/jpeg", "jpg");

    public static readonly ImageFormat Png = new("image/png", "png");

    public static readonly ImageFormat WebP = new("image/webp", "webp");

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();

    private static readonly byte[] WebPSignature = "WEBP"u8.ToArray();

    /// <summary>Null when the bytes are not one of the formats the menu shows.</summary>
    public static ImageFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegSignature)) return Jpeg;
        if (header.StartsWith(PngSignature)) return Png;

        // A RIFF container holds WebP, but also WAV and AVI: the second
        // signature, after the four-byte size, is what makes it a picture.
        if (header.Length >= HeaderLength && header.StartsWith(RiffSignature) && header[8..12].SequenceEqual(WebPSignature)) return WebP;

        return null;
    }
}
