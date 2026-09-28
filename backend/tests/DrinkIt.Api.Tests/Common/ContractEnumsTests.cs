using DrinkIt.Api.Common;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// The contract's enums are written by hand, apart from the domain's, so the
/// domain can change without breaking the PWA. The price is forgetting one:
/// these walk every value, so a role or a status added to the domain fails
/// here instead of on the first request that carries it.
/// </summary>
public class ContractEnumsTests
{
    [Fact]
    public void ToContract_ForEveryStaffRole_KeepsItsName()
    {
        foreach (StaffRole role in Enum.GetValues<StaffRole>())
        {
            Assert.Equal(role.ToString(), role.ToContract().ToString());
        }
    }

    [Fact]
    public void ToDomain_ForEveryStaffRoleName_ComesBackAsTheSameRole()
    {
        foreach (StaffRoleName name in Enum.GetValues<StaffRoleName>())
        {
            Assert.Equal(name, name.ToDomain().ToContract());
        }
    }

    // A cart never leaves the phone, so no customer answer can carry one.
    [Fact]
    public void ToCustomerStatus_ForEveryStatusButCart_KeepsItsName()
    {
        foreach (OrderStatus status in Enum.GetValues<OrderStatus>().Where(status => status != OrderStatus.Cart))
        {
            Assert.Equal(status.ToString(), status.ToCustomerStatus().ToString());
        }
    }

    [Theory]
    [InlineData(OrderStatus.Queued)]
    [InlineData(OrderStatus.InPreparation)]
    [InlineData(OrderStatus.Ready)]
    public void ToKdsStatus_ForWhatTheBoardShows_KeepsItsName(OrderStatus status)
    {
        Assert.Equal(status.ToString(), status.ToKdsStatus().ToString());
    }

    // The queue query only returns those three; anything else reaching the
    // board is a bug, and it should say so rather than draw a wrong column.
    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Delivered)]
    public void ToKdsStatus_ForWhatTheBoardNeverShows_Throws(OrderStatus status)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => status.ToKdsStatus());
    }
}
