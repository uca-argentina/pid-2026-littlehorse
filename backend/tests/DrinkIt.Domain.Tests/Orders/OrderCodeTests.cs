using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Domain.Tests.Orders;

/// <summary>
/// The code somebody shouts across a bar to claim their drinks. It is read out
/// loud in the noisiest room the app will ever run in, so its shape is a rule
/// and not a formatting detail.
/// </summary>
public class OrderCodeTests
{
    [Fact]
    public void First_Always_StartsAtTheBeginningOfTheAlphabet()
    {
        Assert.Equal("A-0000", OrderCode.First.Value);
    }

    [Fact]
    public void Next_WhenThereIsRoomInTheBlock_RaisesTheNumber()
    {
        OrderCode code = OrderCode.Parse("A-0000").Next();

        Assert.Equal("A-0001", code.Value);
    }

    // The four digits are a block, not a counter that grows a fifth one.
    [Fact]
    public void Next_WhenTheBlockIsFull_MovesToTheNextLetter()
    {
        OrderCode code = OrderCode.Parse("A-9999").Next();

        Assert.Equal("B-0000", code.Value);
    }

    /// <summary>
    /// 260.000 orders later it starts over, which is what she asked for: a
    /// venue that sold that many has nobody still waiting on an A-0000 from
    /// the first round.
    /// </summary>
    [Fact]
    public void Next_WhenTheLastCodeIsReached_StartsOverFromTheFirst()
    {
        OrderCode code = OrderCode.Parse("Z-9999").Next();

        Assert.Equal("A-0000", code.Value);
    }

    [Theory]
    [InlineData("A-0000")]
    [InlineData("K-4821")]
    [InlineData("Z-9999")]
    public void Parse_WhenTheShapeIsRight_ReadsItBack(string value)
    {
        Assert.Equal(value, OrderCode.Parse(value).Value);
    }

    /// <summary>
    /// The hyphen is what tells a letter from a digit when the code is said out
    /// loud: with it, the O of "O-0000" can only be the letter. Without it the
    /// whole point of the shape is gone, so a code missing it is not a code.
    /// </summary>
    [Theory]
    [InlineData("A0000")]
    [InlineData("a-0000")]
    [InlineData("1-0000")]
    [InlineData("A-000")]
    [InlineData("A-00000")]
    [InlineData("A-00A0")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WhenTheShapeIsWrong_ThrowsInvalidCode(string value)
    {
        DomainException error = Assert.Throws<DomainException>(() => OrderCode.Parse(value));

        Assert.Equal(OrderCode.ErrorCodes.Invalid, error.Code);
    }

    /// <summary>
    /// What a code typed by somebody else goes through.
    /// </summary>
    /// <remarks>
    /// A code that arrives in a URL is not a broken invariant — it is a stranger
    /// guessing, or a link that got cut in half in a chat app. That is an answer
    /// of "no such order", not an exception, so there is a way to ask without
    /// one being thrown.
    /// </remarks>
    [Theory]
    [InlineData("A-0000")]
    [InlineData("K-4821")]
    [InlineData("Z-9999")]
    public void TryParse_WhenTheShapeIsRight_ReadsItBack(string value)
    {
        Assert.True(OrderCode.TryParse(value, out OrderCode? code));
        Assert.Equal(value, code!.Value);
    }

    [Theory]
    [InlineData("A0000")]
    [InlineData("a-0000")]
    [InlineData("k-4821")]
    [InlineData("1-0000")]
    [InlineData("A-000")]
    [InlineData("A-00000")]
    [InlineData("A-00A0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_WhenTheShapeIsWrong_SaysSoInsteadOfThrowing(string? value)
    {
        Assert.False(OrderCode.TryParse(value, out OrderCode? code));
        Assert.Null(code);
    }

    // Two codes are the same code when they read the same: what the bar
    // compares is the string on the screen, not which object made it.
    [Fact]
    public void Equals_WhenBothReadTheSame_AreTheSameCode()
    {
        Assert.Equal(OrderCode.Parse("K-4821"), OrderCode.Parse("K-4821"));
        Assert.NotEqual(OrderCode.Parse("K-4821"), OrderCode.Parse("K-4822"));
    }
}
