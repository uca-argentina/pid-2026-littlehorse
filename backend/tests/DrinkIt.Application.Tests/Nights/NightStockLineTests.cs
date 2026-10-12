using DrinkIt.Application.Nights;

namespace DrinkIt.Application.Tests.Nights;

public class NightStockLineTests
{
    private static readonly Guid GinTonic = Guid.CreateVersion7();

    // Criterion 1: the night that is about to start, with nothing after it.
    // What it starts with is what is on the shelf right now.
    [Fact]
    public void From_WhenNothingMovedInThisNightOrLater_CarriesWhatIsOnTheShelf()
    {
        NightStockLine line = NightStockLine.From(GinTonic, "Gin Tonic", shelf: 20, added: 0, sold: 0, netOfLaterNights: 0);

        Assert.Equal(20, line.Carried);
        Assert.Equal(20, line.Loaded);
        Assert.Equal(20, line.Left);
    }

    // Started with 20, loaded 12, sold 7: the shelf holds 25 now.
    [Fact]
    public void From_WhenTheNightLoadedAndSold_WorksBackToWhatItStartedWith()
    {
        NightStockLine line = NightStockLine.From(GinTonic, "Gin Tonic", shelf: 25, added: 12, sold: 7, netOfLaterNights: 0);

        Assert.Equal(20, line.Carried);
        Assert.Equal(32, line.Loaded);
        Assert.Equal(7, line.Sold);
        Assert.Equal(25, line.Left);
    }

    // Criterion 3: Friday, looked at on Sunday. Saturday loaded 10 and sold 4
    // since, so the shelf moved 6 units past what Friday left.
    [Fact]
    public void From_WhenLaterNightsMovedTheShelf_LeavesThemOutOfThisNight()
    {
        NightStockLine line = NightStockLine.From(GinTonic, "Gin Tonic", shelf: 31, added: 12, sold: 7, netOfLaterNights: 6);

        Assert.Equal(20, line.Carried);
        Assert.Equal(32, line.Loaded);
        Assert.Equal(25, line.Left);
    }
}
