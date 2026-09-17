using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;

namespace DrinkIt.Application.Tests.Orders;

/// <summary>
/// Who the bar calls for the order. Criterion 3 of US-11 only asks that an
/// order cannot be confirmed without a name; asking for the surname too is a
/// decision of ours, so that the ticket carries somebody the bar can actually
/// find in a crowd.
/// </summary>
public class CustomerNamePolicyTests
{
    [Fact]
    public void Read_WhenItIsAFullName_HandsItBack()
    {
        Result<string> result = CustomerNamePolicy.Read("María Quadro");

        Assert.True(result.IsSuccess);
        Assert.Equal("María Quadro", result.Value);
    }

    [Fact]
    public void Read_WhenThereAreMoreThanTwoWords_HandsItBack()
    {
        Assert.True(CustomerNamePolicy.Read("María Fernanda Quadro").IsSuccess);
    }

    // What somebody types at 3 AM on a phone: spare spaces everywhere. The bar
    // gets one name with single spaces.
    [Fact]
    public void Read_WhenItIsPaddedWithSpaces_TidiesIt()
    {
        Result<string> result = CustomerNamePolicy.Read("   María    Quadro  ");

        Assert.Equal("María Quadro", result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Read_WhenThereIsNothing_FailsAsRequired(string? name)
    {
        Result<string> result = CustomerNamePolicy.Read(name);

        Assert.Equal(CustomerNamePolicy.Required.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData("Euge")]
    [InlineData("María")]
    public void Read_WhenThereIsOnlyOneWord_AsksForTheSurname(string name)
    {
        Result<string> result = CustomerNamePolicy.Read(name);

        Assert.Equal(CustomerNamePolicy.NeedsASurname.Code, result.Error!.Code);
    }

    // Accents and the ñ are letters like any other: half the surnames here have
    // one, and a rule that rejects Muñoz is a broken rule.
    [Theory]
    [InlineData("María Muñoz")]
    [InlineData("Ángeles Iñíguez")]
    public void Read_WhenItCarriesAccents_HandsItBack(string name)
    {
        Assert.True(CustomerNamePolicy.Read(name).IsSuccess);
    }

    [Theory]
    [InlineData("Euge 21")]
    [InlineData("Ana D'Angelo")]
    [InlineData("Luz García-López")]
    [InlineData("Meg 🍸 Ryan")]
    [InlineData("<script>alert(1)</script> x")]
    public void Read_WhenItCarriesAnythingButLetters_FailsAsStrange(string name)
    {
        Result<string> result = CustomerNamePolicy.Read(name);

        Assert.Equal(CustomerNamePolicy.OnlyLetters.Code, result.Error!.Code);
    }

    [Fact]
    public void Read_WhenItIsLongerThanATicketFits_FailsAsTooLong()
    {
        Result<string> result = CustomerNamePolicy.Read(new string('a', 40) + " " + new string('b', 40));

        Assert.Equal(CustomerNamePolicy.TooLong.Code, result.Error!.Code);
    }
}
