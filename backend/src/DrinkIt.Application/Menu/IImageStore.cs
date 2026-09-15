namespace DrinkIt.Application.Menu;

/// <summary>
/// Where the pictures live. The domain only keeps the address this hands back;
/// whether that is a blob container or a folder on disk is the
/// infrastructure's business.
/// </summary>
public interface IImageStore
{
    /// <summary>
    /// Stores the content under that name, replacing whatever was there, and
    /// returns the absolute address a browser can fetch it from.
    /// </summary>
    Task<string> SaveAsync(string name, Stream content, string contentType, CancellationToken cancellationToken);
}
