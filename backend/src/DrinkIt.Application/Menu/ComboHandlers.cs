using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Application.Menu;

/// <summary>One product a combo is made of, and how many of it go in one combo.</summary>
public sealed record ComboPart(Guid ProductId, int Quantity);

public sealed record CreateComboCommand(
    string Name,
    string? Description,
    string? ImageUrl,
    decimal Price,
    Guid CategoryId,
    IReadOnlyCollection<ComboPart> Components);

public sealed record ReplaceComboComponentsCommand(Guid ComboId, IReadOnlyCollection<ComboPart> Components);

/// <summary>
/// US-50: what a combo can be made of, which takes looking the products up, so
/// it is asked here and not in the domain: this venue's own, still on the menu
/// and not combos themselves.
/// </summary>
internal static class ComboParts
{
    public static async Task<Error?> RefusalAsync(
        IReadOnlyCollection<ComboPart> components,
        IProductsForOrdering products,
        CancellationToken cancellationToken)
    {
        Guid[] ids = [.. components.Select(part => part.ProductId).Distinct()];
        IReadOnlyList<Product> found = await products.GetForOrderingAsync(ids, cancellationToken);

        if (found.Count != ids.Length || found.Any(product => !product.IsActive)) return ProductErrors.ComboComponentNotFound;
        if (found.Any(product => product.IsCombo)) return ProductErrors.ComboInsideCombo;

        return null;
    }

    public static (Guid ProductId, int Quantity)[] ForTheDomain(IReadOnlyCollection<ComboPart> components) =>
        [.. components.Select(part => (part.ProductId, part.Quantity))];
}

/// <summary>US-50: adds a combo to the menu of the signed-in administrator's venue.</summary>
public sealed class CreateComboHandler(
    IProductRepository products,
    ICategoryRepository categories,
    IProductsForOrdering parts,
    ICurrentVenue currentVenue)
{
    public async Task<Result<ProductSummary>> HandleAsync(CreateComboCommand command, CancellationToken cancellationToken)
    {
        string name = (command.Name ?? string.Empty).Trim();

        if (await products.NameExistsAsync(name, cancellationToken)) return CreateProductHandler.NameTaken;
        if (!await categories.ExistsAsync(command.CategoryId, cancellationToken)) return ProductErrors.CategoryNotFound;
        if (await ComboParts.RefusalAsync(command.Components, parts, cancellationToken) is Error refusal) return refusal;

        // Whatever is left wrong is a broken domain invariant: the API answers 400.
        Product combo = Product.CreateCombo(
            currentVenue.Id,
            name,
            command.Description,
            command.ImageUrl,
            command.Price,
            command.CategoryId,
            ComboParts.ForTheDomain(command.Components));

        await products.AddAsync(combo, cancellationToken);

        return ProductSummary.Of(combo);
    }
}

/// <summary>
/// US-50, criterion 3: changes what a combo is made of for the orders to come.
/// The ones already confirmed froze theirs.
/// </summary>
public sealed class ReplaceComboComponentsHandler(IProductRepository products, IProductsForOrdering parts)
{
    public async Task<Result<ProductSummary>> HandleAsync(ReplaceComboComponentsCommand command, CancellationToken cancellationToken)
    {
        Product? combo = await products.GetForUpdateAsync(command.ComboId, cancellationToken);

        if (combo is null) return ProductErrors.NotFound;
        if (await ComboParts.RefusalAsync(command.Components, parts, cancellationToken) is Error refusal) return refusal;

        combo.ReplaceComponents(ComboParts.ForTheDomain(command.Components));

        await products.SaveChangesAsync(cancellationToken);

        return ProductSummary.Of(combo);
    }
}
