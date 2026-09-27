using DrinkIt.Domain.Common;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Domain.Tests.Orders;

/// <summary>
/// The secret half of the link somebody follows to watch their order.
/// </summary>
/// <remarks>
/// The code is the order's name — said out loud at the bar, safe in a log. This
/// is the part that proves the person holding the link is the one who ordered,
/// because without an account there is nothing else to prove it with.
/// </remarks>
public class TrackingTokenTests
{
    [Fact]
    public void New_Always_IsLongEnoughToBeUnguessable()
    {
        Assert.Equal(TrackingToken.Length, TrackingToken.New().Value.Length);
    }

    // Hex, lowercase: it travels in a path segment, gets copied between
    // devices and read off screens, so nothing in it can need escaping and no
    // two characters can look alike in a browser's address bar.
    [Fact]
    public void New_Always_IsMadeOfNothingButHexDigits()
    {
        Assert.Matches("^[0-9a-f]+$", TrackingToken.New().Value);
    }

    /// <summary>
    /// The whole point. Two tokens made one after the other share nothing, so
    /// holding one says nothing about anybody else's.
    /// </summary>
    [Fact]
    public void New_WhenManyAreMade_NoTwoAreTheSame()
    {
        string[] many = [.. Enumerable.Range(0, 500).Select(_ => TrackingToken.New().Value)];

        Assert.Equal(many.Length, many.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("9f3c2ba7d81e4c06a1b2c3d4e5f60718")]
    public void Parse_WhenTheShapeIsRight_ReadsItBack(string value)
    {
        Assert.Equal(value, TrackingToken.Parse(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9f3c2ba7")]
    [InlineData("9F3C2BA7D81E4C06A1B2C3D4E5F60718")]
    [InlineData("9f3c2ba7d81e4c06a1b2c3d4e5f60718a")]
    [InlineData("9f3c2ba7-d81e-4c06-a1b2-c3d4e5f6071")]
    public void Parse_WhenTheShapeIsWrong_ThrowsInvalidToken(string value)
    {
        DomainException error = Assert.Throws<DomainException>(() => TrackingToken.Parse(value));

        Assert.Equal(TrackingToken.ErrorCodes.Invalid, error.Code);
    }

    /// <summary>
    /// Compared in constant time: a comparison that stops at the first wrong
    /// character tells whoever is guessing how much of it they got right, and
    /// this is the one value in the system worth guessing.
    /// </summary>
    [Fact]
    public void Matches_WhenItIsTheSameToken_SaysSo()
    {
        TrackingToken token = TrackingToken.Parse("9f3c2ba7d81e4c06a1b2c3d4e5f60718");

        Assert.True(token.Matches("9f3c2ba7d81e4c06a1b2c3d4e5f60718"));
        Assert.False(token.Matches("9f3c2ba7d81e4c06a1b2c3d4e5f60719"));
        Assert.False(token.Matches("no es un token"));
        Assert.False(token.Matches(null));
    }
}
