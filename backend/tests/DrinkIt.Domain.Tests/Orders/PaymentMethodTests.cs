using DrinkIt.Domain.Orders;

namespace DrinkIt.Domain.Tests.Orders;

public class PaymentMethodTests
{
    // US-15: the KDS board tells barra from mesa by this, and only this — no
    // separate delivery-type field exists, so the rule has one place to live.
    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Digital)]
    public void IsForTable_WhenPaidAtTheBar_IsFalse(PaymentMethod method)
    {
        Assert.False(method.IsForTable());
    }

    [Fact]
    public void IsForTable_WhenPaidFromTheVipBalance_IsTrue()
    {
        Assert.True(PaymentMethod.VipBalance.IsForTable());
    }
}
