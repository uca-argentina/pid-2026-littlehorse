namespace DrinkIt.Domain.Menu;

/// <summary>
/// The three tabs a customer's menu is split into (US-14). A fixed list in
/// code, not a table: the venue does not invent its own categories, so there
/// is nothing here for an administration screen to manage.
/// </summary>
/// <remarks>
/// Values are explicit and start at 1, same reasoning as
/// <see cref="DrinkIt.Domain.Staff.StaffRole"/>: default(ProductCategory) must
/// not be a silently valid category.
/// </remarks>
public enum ProductCategory
{
    Drink = 1,
    Beer = 2,
    NonAlcoholic = 3,
}
