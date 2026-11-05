using DrinkIt.Domain.Common;
using DrinkIt.Domain.StampCards;

namespace DrinkIt.Domain.Tests.StampCards;

/// <summary>US-57: how many stamps earn the reward, and which product is given.</summary>
public class StampCardRuleTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly Guid Gin = Guid.CreateVersion7();

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void Create_WhenValid_KeepsWhatWasGiven()
    {
        StampCardRule rule = StampCardRule.Create(AVenue, 5, Gin, true);

        Assert.Equal(AVenue, rule.VenueId);
        Assert.Equal(5, rule.StampsForReward);
        Assert.Equal(Gin, rule.RewardProductId);
        Assert.True(rule.IsActive);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(51)]
    public void Create_WhenStampsAreOutOfRange_Throws(int stamps)
    {
        Assert.Equal(StampCardRule.ErrorCodes.StampsOutOfRange, CodeOf(() => StampCardRule.Create(AVenue, stamps, Gin, true)));
    }

    [Fact]
    public void Create_WithTwoStamps_IsTheSmallestRuleThatIsAllowed()
    {
        Assert.Equal(2, StampCardRule.Create(AVenue, 2, Gin, true).StampsForReward);
    }

    [Fact]
    public void Create_WithoutAReward_Throws()
    {
        Assert.Equal(StampCardRule.ErrorCodes.RewardRequired, CodeOf(() => StampCardRule.Create(AVenue, 5, Guid.Empty, true)));
    }

    [Fact]
    public void Create_WithoutAVenue_Throws()
    {
        Assert.Equal(StampCardRule.ErrorCodes.VenueRequired, CodeOf(() => StampCardRule.Create(Guid.Empty, 5, Gin, true)));
    }

    [Fact]
    public void Update_WhenValid_ReplacesTheRuleAndTheSwitch()
    {
        StampCardRule rule = StampCardRule.Create(AVenue, 5, Gin, true);
        Guid fernet = Guid.CreateVersion7();

        rule.Update(8, fernet, false);

        Assert.Equal(8, rule.StampsForReward);
        Assert.Equal(fernet, rule.RewardProductId);
        Assert.False(rule.IsActive);
    }

    [Fact]
    public void Update_WhenInvalid_ChangesNothing()
    {
        StampCardRule rule = StampCardRule.Create(AVenue, 5, Gin, true);

        Assert.Equal(StampCardRule.ErrorCodes.StampsOutOfRange, CodeOf(() => rule.Update(1, Gin, false)));

        Assert.Equal(5, rule.StampsForReward);
        Assert.True(rule.IsActive);
    }
}
