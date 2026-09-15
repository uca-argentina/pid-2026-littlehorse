namespace DrinkIt.Infrastructure.Menu;

/// <summary>
/// Where the product pictures go. In Azure, a storage account; on a laptop,
/// Azurite from docker-compose.yml, which the development connection string
/// "UseDevelopmentStorage=true" points at.
/// </summary>
public sealed class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// One container for every venue's pictures: the blob name carries the
    /// product id, and a product belongs to one venue.
    /// </summary>
    public string ContainerName { get; set; } = "product-images";
}
