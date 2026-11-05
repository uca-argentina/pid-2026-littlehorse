using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Domain.Tests.Orders;

/// <summary>US-59: the reward is a line of its own, at zero, next to something paid.</summary>
public class OrderRewardTests
{
    private static readonly Guid Gin = Guid.CreateVersion7();

    private static readonly Guid Fernet = Guid.CreateVersion7();

    private static Order AnOrder(Guid? card, params NewOrderItem[] items) =>
        Order.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), "María", OrderCode.Parse("K-4821"), items, card);

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void Place_WithARewardNextToAPaidLine_CostsOnlyThePaidLine()
    {
        Order order = AnOrder(null, new NewOrderItem(Fernet, "Fernet", 4000m, 2, null), NewOrderItem.Reward(Gin, "Gin Tonic", 5));

        Assert.Equal(8000m, order.Total);
        Assert.Equal(2, order.Items.Count);
    }

    [Fact]
    public void Place_WithARewardLine_MarksItAsRewardWithWhatItCost()
    {
        Order order = AnOrder(null, new NewOrderItem(Fernet, "Fernet", 4000m, 1, null), NewOrderItem.Reward(Gin, "Gin Tonic", 5));

        OrderItem reward = order.Items.Single(item => item.IsReward);

        Assert.Equal(0m, reward.UnitPrice);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(5, reward.StampsSpent);
        Assert.Equal(0m, reward.Saved);
        Assert.False(order.Items.Single(item => !item.IsReward).IsReward);
    }

    // A paid Gin Tonic and the reward Gin Tonic take units off the bar together.
    [Fact]
    public void Place_WhenTheRewardIsTheSameProductAsAPaidLine_BothAreAllowedAndUnitsAddUp()
    {
        Order order = AnOrder(null, new NewOrderItem(Gin, "Gin Tonic", 5000m, 2, null), NewOrderItem.Reward(Gin, "Gin Tonic", 5));

        Assert.Equal(3, order.UnitsByProduct()[Gin]);
    }

    [Fact]
    public void Place_WithOnlyARewardLine_Throws()
    {
        Assert.Equal(Order.ErrorCodes.RewardNeedsAPaidLine, CodeOf(() => AnOrder(null, NewOrderItem.Reward(Gin, "Gin Tonic", 5))));
    }

    [Fact]
    public void Place_WithTwoRewardLines_Throws()
    {
        Assert.Equal(
            Order.ErrorCodes.DuplicateItem,
            CodeOf(() => AnOrder(null, new NewOrderItem(Fernet, "Fernet", 4000m, 1, null), NewOrderItem.Reward(Gin, "Gin Tonic", 5), NewOrderItem.Reward(Gin, "Gin Tonic", 5))));
    }

    [Fact]
    public void Place_WithARewardThatCostsNoStamps_Throws()
    {
        Assert.Equal(
            Order.ErrorCodes.RewardCostNotPositive,
            CodeOf(() => AnOrder(null, new NewOrderItem(Fernet, "Fernet", 4000m, 1, null), NewOrderItem.Reward(Gin, "Gin Tonic", 0))));
    }

    // The exception for zero is the reward and nothing else.
    [Fact]
    public void Place_WithAZeroPricedLineThatIsNotAReward_StillThrows()
    {
        Assert.Equal(
            Order.ErrorCodes.PriceNotPositive,
            CodeOf(() => AnOrder(null, new NewOrderItem(Fernet, "Fernet", 4000m, 1, null), new NewOrderItem(Gin, "Gin Tonic", 0m, 1, null))));
    }

    [Fact]
    public void Place_WithTheSameProductTwiceAndNoReward_StillThrows()
    {
        Assert.Equal(
            Order.ErrorCodes.DuplicateItem,
            CodeOf(() => AnOrder(null, new NewOrderItem(Gin, "Gin Tonic", 5000m, 1, null), new NewOrderItem(Gin, "Gin Tonic", 5000m, 1, null))));
    }

    [Fact]
    public void Place_WithAStampCard_RemembersWhichOne()
    {
        Guid card = Guid.CreateVersion7();

        Assert.Equal(card, AnOrder(card, new NewOrderItem(Fernet, "Fernet", 4000m, 1, null)).StampCardId);
        Assert.Null(AnOrder(null, new NewOrderItem(Fernet, "Fernet", 4000m, 1, null)).StampCardId);
    }
}
