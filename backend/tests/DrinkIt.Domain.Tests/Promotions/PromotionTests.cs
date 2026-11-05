using DrinkIt.Domain.Common;
using DrinkIt.Domain.Promotions;

namespace DrinkIt.Domain.Tests.Promotions;

/// <summary>US-54: a percentage off some products, on some days, in a window of the day.</summary>
public class PromotionTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static readonly Guid Gin = Guid.CreateVersion7();

    private static readonly Guid Fernet = Guid.CreateVersion7();

    // 2026-11-06 is a Friday, 2026-11-07 a Saturday.
    private static DateTime Friday(int hour, int minute = 0) => new(2026, 11, 6, hour, minute, 0);

    private static DateTime Saturday(int hour, int minute = 0) => new(2026, 11, 7, hour, minute, 0);

    private static Promotion AFridayNightPromotion() =>
        Promotion.Create(AVenue, "Happy hour", 30, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin]);

    private static string CodeOf(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void Create_WhenValid_IsActiveAndKeepsWhatWasGiven()
    {
        Promotion promotion = AFridayNightPromotion();

        Assert.True(promotion.IsActive);
        Assert.Equal("Happy hour", promotion.Name);
        Assert.Equal(30, promotion.DiscountPercent);
        Assert.Equal([DayOfWeek.Friday], promotion.Days);
        Assert.Equal([Gin], promotion.ProductIds);
    }

    [Fact]
    public void IsOnAt_WhenTheWindowCrossesMidnightAndItIsAfterMidnight_BelongsToTheDayItStarted()
    {
        Promotion promotion = AFridayNightPromotion();

        Assert.True(promotion.IsOnAt(Saturday(0, 30)));
    }

    [Fact]
    public void IsOnAt_WhenTheWindowCrossesMidnightAndItIsEarlyOnTheStartDay_IsOff()
    {
        Promotion promotion = AFridayNightPromotion();

        Assert.False(promotion.IsOnAt(Friday(0, 30)));
    }

    [Fact]
    public void IsOnAt_WhenTheWindowCrossesMidnightAndItIsBeforeMidnightOnTheStartDay_IsOn()
    {
        Assert.True(AFridayNightPromotion().IsOnAt(Friday(23, 30)));
    }

    [Fact]
    public void IsOnAt_WhenTheWindowEndsExactlyNow_IsOff()
    {
        Assert.False(AFridayNightPromotion().IsOnAt(Saturday(1, 0)));
    }

    [Fact]
    public void IsOnAt_WhenTheWindowStartsExactlyNow_IsOn()
    {
        Assert.True(AFridayNightPromotion().IsOnAt(Friday(23, 0)));
    }

    [Fact]
    public void IsOnAt_WhenTheWindowStaysInsideTheDay_IsOnlyOnThatDay()
    {
        Promotion promotion = Promotion.Create(AVenue, "Tarde", 10, [DayOfWeek.Friday], new TimeOnly(18, 0), new TimeOnly(20, 0), [Gin]);

        Assert.True(promotion.IsOnAt(Friday(19)));
        Assert.False(promotion.IsOnAt(Friday(20)));
        Assert.False(promotion.IsOnAt(Saturday(19)));
    }

    [Fact]
    public void IsOnAt_WhenDeactivated_IsOff()
    {
        Promotion promotion = AFridayNightPromotion();

        promotion.Deactivate();

        Assert.False(promotion.IsOnAt(Friday(23, 30)));
    }

    [Fact]
    public void Activate_AfterDeactivating_IsOnAgain()
    {
        Promotion promotion = AFridayNightPromotion();

        promotion.Deactivate();
        promotion.Activate();

        Assert.True(promotion.IsOnAt(Friday(23, 30)));
    }

    [Fact]
    public void Covers_OnlyTheChosenProducts()
    {
        Promotion promotion = AFridayNightPromotion();

        Assert.True(promotion.Covers(Gin));
        Assert.False(promotion.Covers(Fernet));
    }

    [Theory]
    [InlineData(30, 4500, 3150)]
    [InlineData(33, 100.05, 67.03)]
    [InlineData(15, 0.10, 0.09)]
    public void PriceOf_RoundsToTwoDecimalsAwayFromZero(int percent, double regular, double expected)
    {
        Promotion promotion = Promotion.Create(AVenue, "P", percent, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin]);

        Assert.Equal((decimal)expected, promotion.PriceOf((decimal)regular));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-5)]
    public void Create_WhenThePercentIsOutOfRange_Throws(int percent)
    {
        string code = CodeOf(() => Promotion.Create(AVenue, "P", percent, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin]));

        Assert.Equal(Promotion.ErrorCodes.DiscountOutOfRange, code);
    }

    [Fact]
    public void Create_WhenThereAreNoDays_Throws()
    {
        string code = CodeOf(() => Promotion.Create(AVenue, "P", 30, [], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin]));

        Assert.Equal(Promotion.ErrorCodes.DaysRequired, code);
    }

    [Fact]
    public void Create_WhenADayIsNotADayOfTheWeek_Throws()
    {
        string code = CodeOf(() => Promotion.Create(AVenue, "P", 30, [(DayOfWeek)9], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin]));

        Assert.Equal(Promotion.ErrorCodes.DayInvalid, code);
    }

    [Fact]
    public void Create_WhenThereAreNoProducts_Throws()
    {
        string code = CodeOf(() => Promotion.Create(AVenue, "P", 30, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(1, 0), []));

        Assert.Equal(Promotion.ErrorCodes.ProductsRequired, code);
    }

    [Fact]
    public void Create_WhenAProductRepeats_Throws()
    {
        string code = CodeOf(() => Promotion.Create(AVenue, "P", 30, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin, Gin]));

        Assert.Equal(Promotion.ErrorCodes.DuplicateProduct, code);
    }

    [Fact]
    public void Create_WhenTheWindowStartsWhereItEnds_Throws()
    {
        string code = CodeOf(() => Promotion.Create(AVenue, "P", 30, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(23, 0), [Gin]));

        Assert.Equal(Promotion.ErrorCodes.WindowEmpty, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenTheNameIsBlank_Throws(string name)
    {
        string code = CodeOf(() => Promotion.Create(AVenue, name, 30, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin]));

        Assert.Equal(Promotion.ErrorCodes.NameRequired, code);
    }

    [Fact]
    public void Create_WhenTheNameIsTooLong_Throws()
    {
        string code = CodeOf(() => Promotion.Create(AVenue, new string('a', Promotion.NameMaxLength + 1), 30, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin]));

        Assert.Equal(Promotion.ErrorCodes.NameLength, code);
    }

    [Fact]
    public void Create_WithoutAVenue_Throws()
    {
        string code = CodeOf(() => Promotion.Create(Guid.Empty, "P", 30, [DayOfWeek.Friday], new TimeOnly(23, 0), new TimeOnly(1, 0), [Gin]));

        Assert.Equal(Promotion.ErrorCodes.VenueRequired, code);
    }

    [Fact]
    public void Update_WhenValid_ReplacesEverythingButTheVenueAndTheSwitch()
    {
        Promotion promotion = AFridayNightPromotion();

        promotion.Update("Sabado", 20, [DayOfWeek.Saturday], new TimeOnly(20, 0), new TimeOnly(22, 0), [Fernet, Gin]);

        Assert.Equal("Sabado", promotion.Name);
        Assert.Equal(20, promotion.DiscountPercent);
        Assert.Equal([DayOfWeek.Saturday], promotion.Days);
        Assert.Equal(new TimeOnly(20, 0), promotion.StartsAt);
        Assert.Equal(new TimeOnly(22, 0), promotion.EndsAt);
        Assert.Equal(2, promotion.ProductIds.Count);
        Assert.Contains(Fernet, promotion.ProductIds);
        Assert.Contains(Gin, promotion.ProductIds);
        Assert.True(promotion.IsActive);
    }

    [Fact]
    public void Update_WhenInvalid_ChangesNothing()
    {
        Promotion promotion = AFridayNightPromotion();

        Assert.Throws<DomainException>(() => promotion.Update("Sabado", 150, [DayOfWeek.Saturday], new TimeOnly(20, 0), new TimeOnly(22, 0), [Fernet]));

        Assert.Equal("Happy hour", promotion.Name);
        Assert.Equal([Gin], promotion.ProductIds);
    }
}
