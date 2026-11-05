using DrinkIt.Domain.Orders;

namespace DrinkIt.Domain.Tests.Orders;

/// <summary>US-51: what an order takes from a bar, combos taken apart.</summary>
public class OrderComboTests
{
    private static readonly Guid Fernet = Guid.CreateVersion7();

    private static readonly Guid Ice = Guid.CreateVersion7();

    private static readonly Guid Combo = Guid.CreateVersion7();

    private static NewOrderItem TwoCombos() => new(
        Combo,
        "Combo Fernet",
        9000m,
        2,
        null,
        [new NewOrderItemComponent(Fernet, "Fernet", 2), new NewOrderItemComponent(Ice, "Hielo", 1)]);

    private static Order AnOrder(params NewOrderItem[] items) =>
        Order.Place(Guid.CreateVersion7(), Guid.CreateVersion7(), "María", OrderCode.Parse("K-4821"), items);

    [Fact]
    public void UnitsByProduct_WhenAnItemIsACombo_TakesItApart()
    {
        IReadOnlyDictionary<Guid, int> units = AnOrder(TwoCombos()).UnitsByProduct();

        Assert.Equal(2, units.Count);
        Assert.Equal(4, units[Fernet]);
        Assert.Equal(2, units[Ice]);
    }

    // A combo with Fernet and a loose Fernet read line by line would each pass
    // a check that together they fail.
    [Fact]
    public void UnitsByProduct_WhenAComboAndALooseItemShareAProduct_AddsThemUp()
    {
        Order order = AnOrder(TwoCombos(), new NewOrderItem(Fernet, "Fernet", 4000m, 3, null));

        Assert.Equal(7, order.UnitsByProduct()[Fernet]);
    }

    [Fact]
    public void UnitsByProduct_WhenThereAreNoCombos_IsItsOwnLines()
    {
        Order order = AnOrder(new NewOrderItem(Fernet, "Fernet", 4000m, 3, null));

        Assert.Equal(3, Assert.Single(order.UnitsByProduct()).Value);
    }

    // Criterion 2: the customer asked for the combo, and that is what they can take out.
    [Fact]
    public void ItemThatNeeds_WhenTheProductIsAComponent_IsTheCombo()
    {
        Assert.Equal("Combo Fernet", AnOrder(TwoCombos()).ItemThatNeeds(Ice).ProductName);
    }

    [Fact]
    public void ItemThatNeeds_WhenTheProductIsALooseLine_IsThatLine()
    {
        Order order = AnOrder(TwoCombos(), new NewOrderItem(Fernet, "Fernet", 4000m, 3, null));

        Assert.Equal("Fernet", order.ItemThatNeeds(Fernet).ProductName);
    }

    // Criterion 3: the recipe as it was when it was confirmed.
    [Fact]
    public void Place_WhenAnItemIsACombo_FreezesItsComponentsWithTheirNames()
    {
        OrderItem item = AnOrder(TwoCombos()).Items.Single();

        Assert.Contains(item.Components, component => component.ProductId == Fernet && component.ProductName == "Fernet" && component.Quantity == 2);
        Assert.Equal(18000m, item.Total);
    }
}
