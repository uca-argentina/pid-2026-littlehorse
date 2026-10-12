using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Domain.Tests.Nights;

/// <summary>US-37: loading the night's stock.</summary>
public class NightStockTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    private static Night ANight() => Night.Create(
        AVenue,
        "Saturday 10/10",
        Opening,
        Closing,
        [StaffUser.Create(AVenue, "main-bar", "hash", StaffRole.Kds), StaffUser.Create(AVenue, "till-1", "hash", StaffRole.Cashier)]);

    private static Product AGinTonicWith(int stock, Guid? venueId = null) =>
        Product.Create(venueId ?? AVenue, "Gin Tonic", null, null, 9500m, stock, Guid.CreateVersion7());

    [Fact]
    public void AdjustStock_WhenNightIsUpcoming_RecordsTheChangeAgainstTheNight()
    {
        Night night = ANight();
        Product ginTonic = AGinTonicWith(20);

        NightStockAdjustment adjustment = night.AdjustStock(ginTonic, 12, Opening.AddHours(-5));

        Assert.NotEqual(Guid.Empty, adjustment.Id);
        Assert.Equal(AVenue, adjustment.VenueId);
        Assert.Equal(night.Id, adjustment.NightId);
        Assert.Equal(ginTonic.Id, adjustment.ProductId);
        Assert.Equal(12, adjustment.Change);
    }

    [Fact]
    public void AdjustStock_WhenNightIsUnderway_MovesWhatIsLeftOfTheProduct()
    {
        Night night = ANight();
        Product ginTonic = AGinTonicWith(20);

        night.AdjustStock(ginTonic, -5, Opening.AddHours(2));

        Assert.Equal(15, ginTonic.Stock);
    }

    [Fact]
    public void AdjustStock_WhenNightIsOver_ThrowsNightOver()
    {
        Night night = ANight();
        Product ginTonic = AGinTonicWith(20);

        DomainException error = Assert.Throws<DomainException>(() => night.AdjustStock(ginTonic, 12, Closing));

        Assert.Equal(Night.ErrorCodes.NightOver, error.Code);
        Assert.Equal(20, ginTonic.Stock);
    }

    [Fact]
    public void AdjustStock_WhenProductIsFromAnotherVenue_ThrowsProductFromAnotherVenue()
    {
        Night night = ANight();
        Product elsewhere = AGinTonicWith(20, Guid.CreateVersion7());

        DomainException error = Assert.Throws<DomainException>(() => night.AdjustStock(elsewhere, 12, Opening));

        Assert.Equal(Night.ErrorCodes.ProductFromAnotherVenue, error.Code);
        Assert.Equal(20, elsewhere.Stock);
    }

    [Fact]
    public void AdjustStock_WhenChangeIsZero_ThrowsStockChangeZero()
    {
        Night night = ANight();

        DomainException error = Assert.Throws<DomainException>(() => night.AdjustStock(AGinTonicWith(20), 0, Opening));

        Assert.Equal(Product.ErrorCodes.StockChangeZero, error.Code);
    }

    [Fact]
    public void AdjustStock_WhenItTakesAwayMoreThanIsLeft_ThrowsStockNegative()
    {
        Night night = ANight();

        DomainException error = Assert.Throws<DomainException>(() => night.AdjustStock(AGinTonicWith(3), -4, Opening));

        Assert.Equal(Product.ErrorCodes.StockNegative, error.Code);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(7, true)]
    public void HasEndedAt_ComparedWithTheEnd_IsTrueFromTheEndOn(int hoursAfterClosing, bool expected) =>
        Assert.Equal(expected, ANight().HasEndedAt(Closing.AddHours(hoursAfterClosing)));
}
