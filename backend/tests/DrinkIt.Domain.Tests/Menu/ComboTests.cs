using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Domain.Tests.Menu;

/// <summary>US-50: a combo is a product made of others, with no stock of its own.</summary>
public class ComboTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly Guid Fernet = Guid.CreateVersion7();

    private static readonly Guid Ice = Guid.CreateVersion7();

    private static Product ACombo(params (Guid ProductId, int Quantity)[] components) =>
        Product.CreateCombo(AVenue, "Combo Fernet", null, null, 9000m, ACategory.Id, components.Length == 0 ? [(Fernet, 2), (Ice, 1)] : components);

    [Fact]
    public void CreateCombo_WhenValid_KeepsItsComponentsAndHoldsNoStock()
    {
        Product combo = ACombo();

        Assert.True(combo.IsCombo);
        Assert.Equal(0, combo.Stock);
        Assert.Equal(9000m, combo.Price);
        Assert.Contains(combo.Components, component => component.ProductId == Fernet && component.Quantity == 2);
        Assert.Contains(combo.Components, component => component.ProductId == Ice && component.Quantity == 1);
    }

    // Having no stock of its own must not read as having run out of it: whether
    // it can be served is a question about its components.
    [Fact]
    public void IsSoldOut_WhenItIsACombo_IsFalse()
    {
        Assert.False(ACombo().IsSoldOut);
    }

    [Fact]
    public void MarkAvailable_WhenItIsACombo_TurnsTheSwitchBackOn()
    {
        Product combo = ACombo();
        combo.MarkUnavailable();

        combo.MarkAvailable();

        Assert.True(combo.IsAvailable);
    }

    // Criterion 2. "Two units" lets a bucket of four equal beers be a combo.
    [Fact]
    public void CreateCombo_WhenItHasFourOfTheSameProduct_IsAccepted()
    {
        Assert.True(ACombo((Fernet, 4)).IsCombo);
    }

    [Fact]
    public void CreateCombo_WhenItAddsUpToOneUnit_ThrowsTooFewUnits()
    {
        DomainException error = Assert.Throws<DomainException>(() => ACombo((Fernet, 1)));

        Assert.Equal(Product.ErrorCodes.ComboTooFewUnits, error.Code);
    }

    [Fact]
    public void CreateCombo_WhenAQuantityIsNotPositive_ThrowsQuantityNotPositive()
    {
        DomainException error = Assert.Throws<DomainException>(() => ACombo((Fernet, 3), (Ice, 0)));

        Assert.Equal(Product.ErrorCodes.ComboQuantityNotPositive, error.Code);
    }

    [Fact]
    public void CreateCombo_WhenAProductIsListedTwice_ThrowsDuplicateComponent()
    {
        DomainException error = Assert.Throws<DomainException>(() => ACombo((Fernet, 1), (Fernet, 1)));

        Assert.Equal(Product.ErrorCodes.ComboDuplicateComponent, error.Code);
    }

    [Fact]
    public void AdjustStock_WhenItIsACombo_ThrowsHasNoStock()
    {
        DomainException error = Assert.Throws<DomainException>(() => ACombo().AdjustStock(5));

        Assert.Equal(Product.ErrorCodes.ComboHasNoStock, error.Code);
    }

    [Fact]
    public void ReplaceComponents_WhenItKeepsOneAndChangesAnother_LeavesExactlyTheNewOnes()
    {
        Product combo = ACombo();
        Guid cola = Guid.CreateVersion7();

        combo.ReplaceComponents([(Fernet, 3), (cola, 1)]);

        Assert.Equal(2, combo.Components.Count);
        Assert.Equal(3, combo.Components.Single(component => component.ProductId == Fernet).Quantity);
        Assert.Contains(combo.Components, component => component.ProductId == cola);
        Assert.DoesNotContain(combo.Components, component => component.ProductId == Ice);
    }

    [Fact]
    public void ReplaceComponents_WhenTheNewOnesBreakARule_LeavesTheOldOnes()
    {
        Product combo = ACombo();

        Assert.Throws<DomainException>(() => combo.ReplaceComponents([(Fernet, 1)]));

        Assert.Equal(2, combo.Components.Count);
    }

    [Fact]
    public void ReplaceComponents_WhenItIsNotACombo_ThrowsNotACombo()
    {
        Product gin = Product.Create(AVenue, "Gin Tonic", null, null, 4500m, 20, ACategory.Id);

        DomainException error = Assert.Throws<DomainException>(() => gin.ReplaceComponents([(Fernet, 2)]));

        Assert.Equal(Product.ErrorCodes.NotACombo, error.Code);
    }
}
