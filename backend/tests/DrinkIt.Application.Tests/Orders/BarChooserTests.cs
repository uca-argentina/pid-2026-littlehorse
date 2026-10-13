using DrinkIt.Application.Orders;

namespace DrinkIt.Application.Tests.Orders;

/// <summary>US-37: the order goes to the bar with the least work among those that have all of it.</summary>
public class BarChooserTests
{
    private static readonly Guid Gin = Guid.CreateVersion7();
    private static readonly Guid Fernet = Guid.CreateVersion7();

    private static BarSupply ABar(int load, int gin, int fernet) =>
        new(Guid.CreateVersion7(), load, new Dictionary<Guid, int> { [Gin] = gin, [Fernet] = fernet });

    private static readonly OrderLine[] TwoGinsAndAFernet = [new(Gin, 2), new(Fernet, 1)];

    [Fact]
    public void Choose_WhenTwoBarsHaveItAll_PicksTheOneWithLessWork()
    {
        BarSupply busy = ABar(load: 5, gin: 10, fernet: 10);
        BarSupply quiet = ABar(load: 1, gin: 10, fernet: 10);

        Assert.Equal(quiet, BarChooser.Choose([busy, quiet], TwoGinsAndAFernet));
    }

    [Fact]
    public void Choose_WhenOnlyTheBusierBarHasItAll_PicksThatOne()
    {
        BarSupply busy = ABar(load: 5, gin: 10, fernet: 10);
        BarSupply quiet = ABar(load: 1, gin: 1, fernet: 10);

        Assert.Equal(busy, BarChooser.Choose([busy, quiet], TwoGinsAndAFernet));
    }

    // Same work: the first one given, so the answer does not change between two asks.
    [Fact]
    public void Choose_WhenTheBarsAreEquallyBusy_PicksTheFirst()
    {
        BarSupply first = ABar(load: 2, gin: 10, fernet: 10);
        BarSupply second = ABar(load: 2, gin: 10, fernet: 10);

        Assert.Equal(first, BarChooser.Choose([first, second], TwoGinsAndAFernet));
    }

    // One bar has the gins, the other the fernet: an order is not split.
    [Fact]
    public void Choose_WhenNoBarHasItAll_IsNull()
    {
        BarSupply gins = ABar(load: 0, gin: 10, fernet: 0);
        BarSupply fernets = ABar(load: 0, gin: 0, fernet: 10);

        Assert.Null(BarChooser.Choose([gins, fernets], TwoGinsAndAFernet));
    }

    [Fact]
    public void Missing_WhenNoBarHasEnoughOfADrink_NamesThatDrink()
    {
        BarSupply bar = ABar(load: 0, gin: 1, fernet: 10);

        Assert.Equal(Gin, BarChooser.Missing([bar], TwoGinsAndAFernet));
    }

    // Each drink is somewhere, just not together: named is what the bar that
    // came closest lacks.
    [Fact]
    public void Missing_WhenTheDrinksAreSplitAcrossBars_NamesWhatTheClosestBarLacks()
    {
        BarSupply gins = ABar(load: 0, gin: 10, fernet: 0);
        BarSupply fernets = ABar(load: 0, gin: 1, fernet: 10);

        Assert.Equal(Fernet, BarChooser.Missing([gins, fernets], TwoGinsAndAFernet));
    }
}
