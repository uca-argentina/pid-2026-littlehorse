using DrinkIt.Application.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

public sealed record UpdateProductCommand(Guid ProductId, string Name, string? Description, decimal Price);

/// <summary>
/// US-08: corrects a product's name, description and price. The picture is
/// not here — UploadProductImageHandler already owns it — and neither is
/// stock nor the two switches, each with its own single-purpose handler.
/// </summary>
public sealed class UpdateProductHandler(IProductRepository products)
{
    public async Task<Result<ProductSummary>> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        Product? product = await products.GetForUpdateAsync(command.ProductId, cancellationToken);

        if (product is null) return ProductErrors.NotFound;

        // Trimmed and compared here, before the domain sees it, so a name that
        // only changed case does not collide with itself the same way
        // CreateProductHandler avoids colliding with a completely different
        // product.
        string name = (command.Name ?? string.Empty).Trim();
        bool nameChanged = !string.Equals(name, product.Name, StringComparison.OrdinalIgnoreCase);

        if (nameChanged && await products.NameExistsAsync(name, cancellationToken)) return CreateProductHandler.NameTaken;

        // Whatever is left wrong with the data is a broken domain invariant,
        // and Update throws. The API turns that into a 400; restating the
        // rules here would be a second copy that drifts.
        product.Update(name, command.Description, command.Price);

        await products.SaveChangesAsync(cancellationToken);

        return ProductSummary.Of(product);
    }
}
