using DrinkIt.Domain.Common;
using DrinkIt.Domain.Menu;

namespace DrinkIt.Domain.Tests.Menu;

public class CategoryTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 22, 0, 0, TimeSpan.Zero);

    private static Category ABeerCategory(string name = "Cervezas") => Category.Create(AVenue, name, Now);

    [Fact]
    public void Create_WhenValid_KeepsTheVenueTheNameAndTheMoment()
    {
        Category category = ABeerCategory();

        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal(AVenue, category.VenueId);
        Assert.Equal("Cervezas", category.Name);
        Assert.Equal(Now, category.CreatedAt);
    }

    // Categories always belong to a venue: two venues have to be able to have
    // their own "Cervezas" without seeing each other's.
    [Fact]
    public void Create_WhenVenueIdIsEmpty_ThrowsVenueRequired()
    {
        DomainException error = Assert.Throws<DomainException>(() => Category.Create(Guid.Empty, "Cervezas", Now));

        Assert.Equal(Category.ErrorCodes.VenueRequired, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsBlank_ThrowsNameRequired(string name)
    {
        DomainException error = Assert.Throws<DomainException>(() => ABeerCategory(name));

        Assert.Equal(Category.ErrorCodes.NameRequired, error.Code);
    }

    [Fact]
    public void Create_WhenNameIsLongerThanTheLimit_ThrowsNameLength()
    {
        string tooLong = new('a', Category.NameMaxLength + 1);

        DomainException error = Assert.Throws<DomainException>(() => ABeerCategory(tooLong));

        Assert.Equal(Category.ErrorCodes.NameLength, error.Code);
    }

    [Fact]
    public void Create_WhenNameHasPadding_TrimsIt()
    {
        Assert.Equal("Cervezas", ABeerCategory("  Cervezas  ").Name);
    }
}
