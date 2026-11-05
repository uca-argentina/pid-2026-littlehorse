using DrinkIt.Domain.Common;
using DrinkIt.Domain.StampCards;

namespace DrinkIt.Domain.Tests.StampCards;

/// <summary>US-58: the card a phone holds, identified by a secret only the phone knows.</summary>
public class StampCardTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    [Fact]
    public void Issue_Always_GivesALongTokenOfHexDigits()
    {
        StampCard card = StampCard.Issue(AVenue);

        Assert.Equal(StampCard.TokenLength, card.Token.Length);
        Assert.True(StampCard.IsWellShaped(card.Token));
        Assert.Equal(AVenue, card.VenueId);
    }

    [Fact]
    public void Issue_TwoCards_HaveDifferentTokens()
    {
        Assert.NotEqual(StampCard.Issue(AVenue).Token, StampCard.Issue(AVenue).Token);
    }

    [Fact]
    public void Issue_WithoutAVenue_Throws()
    {
        DomainException error = Assert.Throws<DomainException>(() => StampCard.Issue(Guid.Empty));

        Assert.Equal(StampCard.ErrorCodes.VenueRequired, error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void IsWellShaped_WhenItIsNotAToken_IsFalse(string? candidate)
    {
        Assert.False(StampCard.IsWellShaped(candidate));
    }

    // The balance is derived from orders, so claiming only has to leave a
    // trace in the row: that is what the concurrency token reads.
    [Fact]
    public void Claim_EachTime_CountsOneMore()
    {
        StampCard card = StampCard.Issue(AVenue);

        card.Claim();
        card.Claim();

        Assert.Equal(2, card.RewardsClaimed);
    }
}
