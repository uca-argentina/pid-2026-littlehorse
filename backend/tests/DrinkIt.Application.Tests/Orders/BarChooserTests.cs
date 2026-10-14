using DrinkIt.Application.Orders;

namespace DrinkIt.Application.Tests.Orders;

/// <summary>US-37: the order goes to the bar with the least work among those that have all of it.</summary>
public class BarChooserTests
{
    private static readonly Guid Gin = Guid.CreateVersion7();
    private static readonly Guid Fernet = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 2, 30, 0, TimeSpan.Zero);

    /// <summary>A bar whose tablet beat a moment ago, unless told otherwise.</summary>
    private static BarSupply ABar(int load, int gin, int fernet, TimeSpan? lastBeatAgo = null) =>
        new(Guid.CreateVersion7(), load, new Dictionary<Guid, int> { [Gin] = gin, [Fernet] = fernet }, Now - (lastBeatAgo ?? TimeSpan.FromSeconds(5)));

    private static BarSupply ABarThatNeverBeat(int load, int gin, int fernet) =>
        new(Guid.CreateVersion7(), load, new Dictionary<Guid, int> { [Gin] = gin, [Fernet] = fernet }, LastBeatAt: null);

    private static readonly OrderLine[] TwoGinsAndAFernet = [new(Gin, 2), new(Fernet, 1)];

    [Fact]
    public void Choose_WhenTwoBarsHaveItAll_PicksTheOneWithLessWork()
    {
        BarSupply busy = ABar(load: 5, gin: 10, fernet: 10);
        BarSupply quiet = ABar(load: 1, gin: 10, fernet: 10);

        Assert.Equal(quiet, BarChooser.Choose([busy, quiet], TwoGinsAndAFernet, Now));
    }

    [Fact]
    public void Choose_WhenOnlyTheBusierBarHasItAll_PicksThatOne()
    {
        BarSupply busy = ABar(load: 5, gin: 10, fernet: 10);
        BarSupply quiet = ABar(load: 1, gin: 1, fernet: 10);

        Assert.Equal(busy, BarChooser.Choose([busy, quiet], TwoGinsAndAFernet, Now));
    }

    // Same work: the first one given, so the answer does not change between two asks.
    [Fact]
    public void Choose_WhenTheBarsAreEquallyBusy_PicksTheFirst()
    {
        BarSupply first = ABar(load: 2, gin: 10, fernet: 10);
        BarSupply second = ABar(load: 2, gin: 10, fernet: 10);

        Assert.Equal(first, BarChooser.Choose([first, second], TwoGinsAndAFernet, Now));
    }

    // One bar has the gins, the other the fernet: an order is not split.
    [Fact]
    public void Choose_WhenNoBarHasItAll_IsNull()
    {
        BarSupply gins = ABar(load: 0, gin: 10, fernet: 0);
        BarSupply fernets = ABar(load: 0, gin: 0, fernet: 10);

        Assert.Null(BarChooser.Choose([gins, fernets], TwoGinsAndAFernet, Now));
    }

    // US-38, criterion 1: a tablet that went off or lost the network gets
    // nothing while it stays that way, however little work it has.
    [Fact]
    public void Choose_WhenTheQuieterBarIsDisconnected_PicksTheConnectedOne()
    {
        BarSupply busy = ABar(load: 5, gin: 10, fernet: 10);
        BarSupply quiet = ABar(load: 0, gin: 10, fernet: 10, lastBeatAgo: TimeSpan.FromSeconds(61));

        Assert.Equal(busy, BarChooser.Choose([quiet, busy], TwoGinsAndAFernet, Now));
    }

    // A short drop of the network is not a disconnection: a beat within the
    // last minute still counts.
    [Fact]
    public void Choose_WhenABarBeatWithinTheLastMinute_CountsItAsConnected()
    {
        BarSupply busy = ABar(load: 5, gin: 10, fernet: 10);
        BarSupply quiet = ABar(load: 0, gin: 10, fernet: 10, lastBeatAgo: TimeSpan.FromSeconds(59));

        Assert.Equal(quiet, BarChooser.Choose([busy, quiet], TwoGinsAndAFernet, Now));
    }

    [Fact]
    public void Choose_WhenABarNeverBeat_CountsItAsDisconnected()
    {
        BarSupply busy = ABar(load: 5, gin: 10, fernet: 10);
        BarSupply silent = ABarThatNeverBeat(load: 0, gin: 10, fernet: 10);

        Assert.Equal(busy, BarChooser.Choose([silent, busy], TwoGinsAndAFernet, Now));
    }

    // US-38, criterion 3, as decided on 2026-10-14: with every tablet off the
    // order is not refused nor left without a bar. It goes where it would have
    // gone, takes its stock there and waits on that tablet until it is back.
    [Fact]
    public void Choose_WhenEveryBarIsDisconnected_PicksAsIfTheyWereAllConnected()
    {
        BarSupply busy = ABar(load: 5, gin: 10, fernet: 10, lastBeatAgo: TimeSpan.FromMinutes(3));
        BarSupply quiet = ABarThatNeverBeat(load: 1, gin: 10, fernet: 10);

        Assert.Equal(quiet, BarChooser.Choose([busy, quiet], TwoGinsAndAFernet, Now));
    }

    // Same rule when the connected bars cannot serve it: the drinks are only
    // at a bar whose tablet is off, so the order waits there rather than
    // being turned away for a tablet the customer cannot see.
    [Fact]
    public void Choose_WhenOnlyADisconnectedBarHasItAll_PicksThatOne()
    {
        BarSupply connected = ABar(load: 0, gin: 1, fernet: 10);
        BarSupply disconnected = ABarThatNeverBeat(load: 3, gin: 10, fernet: 10);

        Assert.Equal(disconnected, BarChooser.Choose([connected, disconnected], TwoGinsAndAFernet, Now));
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
