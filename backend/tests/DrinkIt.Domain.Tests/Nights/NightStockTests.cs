using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Domain.Tests.Nights;

/// <summary>US-37: loading a bar's stock for the night. A bar is a KDS.</summary>
public class NightStockTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    private static readonly StaffUser MainBar = StaffUser.Create(AVenue, "main-bar", "hash", StaffRole.Kds);
    private static readonly StaffUser Till = StaffUser.Create(AVenue, "till-1", "hash", StaffRole.Cashier);

    private static Night ANight() => Night.Create(AVenue, "Saturday 10/10", Opening, Closing, [MainBar, Till]);

    private static Product AGinTonic(Guid? venueId = null) =>
        Product.Create(venueId ?? AVenue, "Gin Tonic", null, null, 9500m, 0, Guid.CreateVersion7());

    [Fact]
    public void AdjustStock_WhenNightIsUpcoming_RecordsTheChangeAgainstTheNightAndTheBar()
    {
        Night night = ANight();
        Product ginTonic = AGinTonic();

        NightStockAdjustment adjustment = night.AdjustStock(MainBar, ginTonic, 12, Opening.AddHours(-5));

        Assert.NotEqual(Guid.Empty, adjustment.Id);
        Assert.Equal(AVenue, adjustment.VenueId);
        Assert.Equal(night.Id, adjustment.NightId);
        Assert.Equal(MainBar.Id, adjustment.KdsId);
        Assert.Equal(ginTonic.Id, adjustment.ProductId);
        Assert.Equal(12, adjustment.Change);
    }

    [Fact]
    public void AdjustStock_WhenNightIsUnderway_RecordsTakingUnitsOut()
    {
        NightStockAdjustment adjustment = ANight().AdjustStock(MainBar, AGinTonic(), -5, Opening.AddHours(2));

        Assert.Equal(-5, adjustment.Change);
    }

    [Fact]
    public void AdjustStock_WhenNightIsOver_ThrowsNightOver()
    {
        DomainException error = Assert.Throws<DomainException>(() => ANight().AdjustStock(MainBar, AGinTonic(), 12, Closing));

        Assert.Equal(Night.ErrorCodes.NightOver, error.Code);
    }

    [Fact]
    public void AdjustStock_WhenProductIsFromAnotherVenue_ThrowsProductFromAnotherVenue()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => ANight().AdjustStock(MainBar, AGinTonic(Guid.CreateVersion7()), 12, Opening));

        Assert.Equal(Night.ErrorCodes.ProductFromAnotherVenue, error.Code);
    }

    // Only a KDS has a bar to keep stock in.
    [Fact]
    public void AdjustStock_WhenTheAccountIsNotAKds_ThrowsBarRequired()
    {
        DomainException error = Assert.Throws<DomainException>(() => ANight().AdjustStock(Till, AGinTonic(), 12, Opening));

        Assert.Equal(Night.ErrorCodes.BarRequired, error.Code);
    }

    [Fact]
    public void AdjustStock_WhenTheBarDoesNotWorkTheNight_ThrowsBarNotInNight()
    {
        StaffUser vipBar = StaffUser.Create(AVenue, "vip-bar", "hash", StaffRole.Kds);

        DomainException error = Assert.Throws<DomainException>(() => ANight().AdjustStock(vipBar, AGinTonic(), 12, Opening));

        Assert.Equal(Night.ErrorCodes.BarNotInNight, error.Code);
    }

    [Fact]
    public void AdjustStock_WhenChangeIsZero_ThrowsStockChangeZero()
    {
        DomainException error = Assert.Throws<DomainException>(() => ANight().AdjustStock(MainBar, AGinTonic(), 0, Opening));

        Assert.Equal(Night.ErrorCodes.StockChangeZero, error.Code);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(7, true)]
    public void HasEndedAt_ComparedWithTheEnd_IsTrueFromTheEndOn(int hoursAfterClosing, bool expected) =>
        Assert.Equal(expected, ANight().HasEndedAt(Closing.AddHours(hoursAfterClosing)));
}
