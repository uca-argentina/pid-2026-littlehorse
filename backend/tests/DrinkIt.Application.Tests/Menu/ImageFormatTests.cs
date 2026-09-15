using DrinkIt.Application.Menu;

namespace DrinkIt.Application.Tests.Menu;

/// <summary>
/// The format comes from the first bytes, never from the file name or the
/// content type the browser sent: both are whatever the client says they are.
/// </summary>
public class ImageFormatTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static readonly byte[] WebP = [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];

    [Fact]
    public void Detect_WhenTheBytesAreAJpeg_ReturnsJpeg()
    {
        ImageFormat? format = ImageFormat.Detect(Jpeg);

        Assert.Equal("image/jpeg", format?.ContentType);
        Assert.Equal("jpg", format?.Extension);
    }

    [Fact]
    public void Detect_WhenTheBytesAreAPng_ReturnsPng()
    {
        ImageFormat? format = ImageFormat.Detect(Png);

        Assert.Equal("image/png", format?.ContentType);
        Assert.Equal("png", format?.Extension);
    }

    [Fact]
    public void Detect_WhenTheBytesAreAWebP_ReturnsWebP()
    {
        ImageFormat? format = ImageFormat.Detect(WebP);

        Assert.Equal("image/webp", format?.ContentType);
        Assert.Equal("webp", format?.Extension);
    }

    // A RIFF container that is not WebP (a WAV, an AVI) shares the first four
    // bytes: the second signature is what tells them apart.
    [Fact]
    public void Detect_WhenTheBytesAreARiffThatIsNotWebP_ReturnsNothing()
    {
        byte[] wave = [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45];

        Assert.Null(ImageFormat.Detect(wave));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF, 0xD8 })]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    public void Detect_WhenTheBytesAreSomethingElse_ReturnsNothing(byte[] header)
    {
        Assert.Null(ImageFormat.Detect(header));
    }
}
