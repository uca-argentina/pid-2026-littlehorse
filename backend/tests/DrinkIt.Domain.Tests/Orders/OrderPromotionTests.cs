using DrinkIt.Domain.Orders;

namespace DrinkIt.Domain.Tests.Orders;

/// <summary>US-55 and US-56: an item keeps the list price and the promotion it was sold under.</summary>
public class OrderPromotionTests
{
    private static Order AnOrder(params NewOrderItem[] items) =>
        Order.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), "María", OrderCode.Parse("K-4821"), items);

    private static NewOrderItem OnPromotion(decimal regular, decimal price, int quantity) =>
        new(Guid.CreateVersion7(), "Gin Tonic", price, quantity, null, regularPrice: regular, promotionName: "Happy hour");

    [Fact]
    public void Place_WhenAnItemIsOnPromotion_KeepsTheListPriceAndTheName()
    {
        OrderItem item = AnOrder(OnPromotion(4500m, 3150m, 2)).Items.Single();

        Assert.Equal(3150m, item.UnitPrice);
        Assert.Equal(4500m, item.RegularPrice);
        Assert.Equal("Happy hour", item.PromotionName);
    }

    [Fact]
    public void Place_WhenAnItemIsNotOnPromotion_HasNeither()
    {
        OrderItem item = AnOrder(new NewOrderItem(Guid.CreateVersion7(), "Gin Tonic", 4500m, 1, null)).Items.Single();

        Assert.Null(item.RegularPrice);
        Assert.Null(item.PromotionName);
    }

    [Fact]
    public void Savings_AddsWhatEachDiscountedUnitSaved()
    {
        Order order = AnOrder(
            OnPromotion(4500m, 3150m, 2),
            new NewOrderItem(Guid.CreateVersion7(), "Fernet", 4000m, 3, null));

        Assert.Equal(2700m, order.Savings);
        Assert.Equal(6300m + 12000m, order.Total);
    }
}
