using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

public sealed record CreateProductCommand(
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    int Stock,
    ProductCategory Category);

/// <summary>
/// Adds a product to the menu of the venue the signed-in administrator belongs
/// to. The venue comes from <see cref="ICurrentVenue"/>, which reads the token
/// claim, so an administrator cannot load products anywhere else by changing
/// the request.
/// </summary>
public sealed class CreateProductHandler(IProductRepository products, ICurrentVenue currentVenue)
{
    public static readonly Error NameTaken =
        new("product.name_taken", "This venue already sells a product with that name.");

    public async Task<Result<ProductSummary>> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        // Trimmed here, before the availability check, so "Gin Tonic " cannot
        // get past it and collide on the unique index afterwards, where the
        // failure is a 500 and not a readable message.
        string name = (command.Name ?? string.Empty).Trim();

        if (await products.NameExistsAsync(name, cancellationToken)) return NameTaken;

        // Whatever is left wrong with the data is a broken domain invariant, and
        // Create throws. The API turns that into a 400; restating the rules here
        // would be a second copy that drifts.
        Product product = Product.Create(
            currentVenue.Id,
            name,
            command.Description,
            command.ImageUrl,
            command.Price,
            command.Stock,
            command.Category);

        await products.AddAsync(product, cancellationToken);

        return ProductSummary.Of(product);
    }
}
