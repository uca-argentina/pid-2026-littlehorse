using DrinkIt.Domain.Common;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Domain.Tests.Nights;

/// <summary>
/// US-37: how many units of one product a night has. What a sale takes off is
/// written by the repository with one conditional statement (the same reason
/// as <c>Product.Stock</c> before it), so the sold units are not set here:
/// they are whatever the night loaded and no longer has.
/// </summary>
public class NightStockTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();
    private static readonly Guid ANight = Guid.CreateVersion7();
    private static readonly Guid AProduct = Guid.CreateVersion7();

    private static NightStock WithLeft(int units) => NightStock.Open(AVenue, ANight, AProduct, units);

    [Fact]
    public void Open_WhenValid_KeepsTheVenueTheNightAndTheProduct()
    {
        NightStock stock = WithLeft(12);

        Assert.NotEqual(Guid.Empty, stock.Id);
        Assert.Equal(AVenue, stock.VenueId);
        Assert.Equal(ANight, stock.NightId);
        Assert.Equal(AProduct, stock.ProductId);
    }

    [Fact]
    public void Open_WhenUnitsCarryOver_StartsLoadedWithThemAndNoneSold()
    {
        NightStock stock = WithLeft(12);

        Assert.Equal(12, stock.Loaded);
        Assert.Equal(12, stock.Remaining);
        Assert.Equal(0, stock.Sold);
    }

    [Fact]
    public void Open_WhenNothingCarriesOver_StartsSoldOut()
    {
        Assert.True(WithLeft(0).IsSoldOut);
    }

    [Fact]
    public void Open_WhenUnitsCarryOver_IsNotSoldOut()
    {
        Assert.False(WithLeft(1).IsSoldOut);
    }

    [Fact]
    public void Open_WhenVenueIdIsEmpty_ThrowsVenueRequired()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => NightStock.Open(Guid.Empty, ANight, AProduct, 5));

        Assert.Equal(NightStock.ErrorCodes.VenueRequired, error.Code);
    }

    [Fact]
    public void Open_WhenNightIdIsEmpty_ThrowsNightRequired()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => NightStock.Open(AVenue, Guid.Empty, AProduct, 5));

        Assert.Equal(NightStock.ErrorCodes.NightRequired, error.Code);
    }

    [Fact]
    public void Open_WhenProductIdIsEmpty_ThrowsProductRequired()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => NightStock.Open(AVenue, ANight, Guid.Empty, 5));

        Assert.Equal(NightStock.ErrorCodes.ProductRequired, error.Code);
    }

    [Fact]
    public void Open_WhenTheCarriedOverIsNegative_ThrowsStockNegative()
    {
        DomainException error = Assert.Throws<DomainException>(() => WithLeft(-1));

        Assert.Equal(NightStock.ErrorCodes.StockNegative, error.Code);
    }

    [Fact]
    public void Adjust_WhenUnitsArrive_AddsToWhatWasLoadedAndWhatIsLeft()
    {
        NightStock stock = WithLeft(5);

        stock.Adjust(10);

        Assert.Equal(15, stock.Loaded);
        Assert.Equal(15, stock.Remaining);
        Assert.Equal(0, stock.Sold);
    }

    [Fact]
    public void Adjust_WhenItTakesAwayWhatWasLoadedByMistake_LowersBoth()
    {
        NightStock stock = WithLeft(200);

        stock.Adjust(-180);

        Assert.Equal(20, stock.Loaded);
        Assert.Equal(20, stock.Remaining);
        Assert.Equal(0, stock.Sold);
    }

    [Fact]
    public void Adjust_WhenSoldOutAndUnitsArrive_IsNoLongerSoldOut()
    {
        NightStock stock = WithLeft(0);

        stock.Adjust(12);

        Assert.False(stock.IsSoldOut);
    }

    [Fact]
    public void Adjust_WhenItWouldGoBelowZero_ThrowsStockNegativeAndKeepsTheStock()
    {
        NightStock stock = WithLeft(5);

        DomainException error = Assert.Throws<DomainException>(() => stock.Adjust(-6));

        Assert.Equal(NightStock.ErrorCodes.StockNegative, error.Code);
        Assert.Equal(5, stock.Loaded);
        Assert.Equal(5, stock.Remaining);
    }

    [Fact]
    public void Adjust_WhenTheChangeIsZero_ThrowsStockChangeZero()
    {
        DomainException error = Assert.Throws<DomainException>(() => WithLeft(5).Adjust(0));

        Assert.Equal(NightStock.ErrorCodes.StockChangeZero, error.Code);
    }

    [Theory]
    [InlineData(5, 3, true)]
    [InlineData(5, -5, true)]
    [InlineData(5, -6, false)]
    [InlineData(0, 1, true)]
    [InlineData(0, -1, false)]
    public void CanAdjust_WhenComparedWithWhatIsLeft_SaysWhetherItFits(int left, int change, bool fits)
    {
        Assert.Equal(fits, WithLeft(left).CanAdjust(change));
    }
}
