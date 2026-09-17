using DrinkIt.Domain.Common;
using DrinkIt.Domain.Venues;

namespace DrinkIt.Domain.Tests.Venues;

public class VenueTests
{
    [Fact]
    public void Create_WhenSlugIsUrlSafe_StoresIt()
    {
        Venue venue = Venue.Create("Bar Alfa", "bar-alfa");

        Assert.Equal("bar-alfa", venue.Slug);
        Assert.Equal("Bar Alfa", venue.Name);
        Assert.NotEqual(Guid.Empty, venue.Id);
    }

    // The slug is what the customer's QR resolves to: /{venueSlug}/menu.
    // Anything that is not URL-safe produces a broken or ambiguous link.
    [Theory]
    [InlineData("Bar Alfa", Venue.ErrorCodes.SlugNotUrlSafe)]
    [InlineData("BarAlfa", Venue.ErrorCodes.SlugNotUrlSafe)]
    [InlineData("bar_alfa", Venue.ErrorCodes.SlugNotUrlSafe)]
    [InlineData("bar alfa", Venue.ErrorCodes.SlugNotUrlSafe)]
    [InlineData("-bar", Venue.ErrorCodes.SlugNotUrlSafe)]
    [InlineData("bar-", Venue.ErrorCodes.SlugNotUrlSafe)]
    [InlineData("bar--alfa", Venue.ErrorCodes.SlugNotUrlSafe)]
    [InlineData("ba", Venue.ErrorCodes.SlugLength)]
    [InlineData("", Venue.ErrorCodes.SlugRequired)]
    [InlineData("   ", Venue.ErrorCodes.SlugRequired)]
    public void Create_WhenSlugIsInvalid_ThrowsWithTheMatchingCode(string slug, string expectedCode)
    {
        DomainException error = Assert.Throws<DomainException>(() => Venue.Create("Bar Alfa", slug));

        Assert.Equal(expectedCode, error.Code);
    }

    [Theory]
    [InlineData("", Venue.ErrorCodes.NameRequired)]
    [InlineData("   ", Venue.ErrorCodes.NameRequired)]
    public void Create_WhenNameIsInvalid_ThrowsWithTheMatchingCode(string name, string expectedCode)
    {
        DomainException error = Assert.Throws<DomainException>(() => Venue.Create(name, "bar-alfa"));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void Create_WhenNameExceedsTheLimit_ThrowsNameTooLong()
    {
        string tooLong = new('a', 101);

        DomainException error = Assert.Throws<DomainException>(() => Venue.Create(tooLong, "bar-alfa"));

        Assert.Equal(Venue.ErrorCodes.NameTooLong, error.Code);
    }
}
