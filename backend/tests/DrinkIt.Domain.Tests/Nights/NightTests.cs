using DrinkIt.Domain.Common;
using DrinkIt.Domain.Nights;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Domain.Tests.Nights;

public class NightTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    // Saturday 23:00 to Sunday 06:00, Buenos Aires time: a night crosses midnight.
    private static readonly DateTimeOffset Opening = new(2026, 10, 10, 23, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Closing = Opening.AddHours(7);

    private static StaffUser A(StaffRole role, string username) => StaffUser.Create(AVenue, username, "hash", role);

    private static readonly StaffUser MainBar = A(StaffRole.Kds, "main-bar");
    private static readonly StaffUser Till = A(StaffRole.Cashier, "till-1");
    private static readonly StaffUser Martin = A(StaffRole.Waiter, "martin");

    private static Night ANight(params StaffUser[] crew) =>
        Night.Create(AVenue, "Saturday 10/10", Opening, Closing, crew.Length == 0 ? [MainBar, Till] : crew);

    [Fact]
    public void Create_WhenValid_KeepsTheVenueTheNameTheHoursAndTheCrew()
    {
        Night night = ANight(MainBar, Till, Martin);

        Assert.NotEqual(Guid.Empty, night.Id);
        Assert.Equal(AVenue, night.VenueId);
        Assert.Equal("Saturday 10/10", night.Name);
        Assert.Equal(Opening, night.StartsAt);
        Assert.Equal(Closing, night.EndsAt);
        Assert.Equal([MainBar.Id, Till.Id, Martin.Id], night.CrewIds);
    }

    [Fact]
    public void Create_WhenVenueIdIsEmpty_ThrowsVenueRequired()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Night.Create(Guid.Empty, "Saturday 10/10", Opening, Closing, [MainBar, Till]));

        Assert.Equal(Night.ErrorCodes.VenueRequired, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsBlank_ThrowsNameRequired(string name)
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Night.Create(AVenue, name, Opening, Closing, [MainBar, Till]));

        Assert.Equal(Night.ErrorCodes.NameRequired, error.Code);
    }

    [Fact]
    public void Create_WhenNameIsLongerThanTheLimit_ThrowsNameLength()
    {
        string tooLong = new('a', Night.NameMaxLength + 1);

        DomainException error = Assert.Throws<DomainException>(
            () => Night.Create(AVenue, tooLong, Opening, Closing, [MainBar, Till]));

        Assert.Equal(Night.ErrorCodes.NameLength, error.Code);
    }

    [Fact]
    public void Create_WhenNameHasPadding_TrimsIt()
    {
        Night night = Night.Create(AVenue, "  Saturday 10/10  ", Opening, Closing, [MainBar, Till]);

        Assert.Equal("Saturday 10/10", night.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WhenItDoesNotEndAfterItStarts_ThrowsHoursInvalid(int hours)
    {
        DomainException error = Assert.Throws<DomainException>(
            () => Night.Create(AVenue, "Saturday 10/10", Opening, Opening.AddHours(hours), [MainBar, Till]));

        Assert.Equal(Night.ErrorCodes.HoursInvalid, error.Code);
    }

    // US-35: no orders can be prepared without a bar, and none taken in cash
    // without a till.
    [Fact]
    public void Create_WhenCrewHasNoKds_ThrowsKdsRequired()
    {
        DomainException error = Assert.Throws<DomainException>(() => ANight(Till, Martin));

        Assert.Equal(Night.ErrorCodes.KdsRequired, error.Code);
    }

    [Fact]
    public void Create_WhenCrewHasNoCashier_ThrowsCashierRequired()
    {
        DomainException error = Assert.Throws<DomainException>(() => ANight(MainBar, Martin));

        Assert.Equal(Night.ErrorCodes.CashierRequired, error.Code);
    }

    // A waiter is only needed when the night sells VIP tables, and that rule
    // arrives with them (US-40).
    [Fact]
    public void Create_WhenCrewHasNoWaiter_IsAccepted()
    {
        Night night = ANight(MainBar, Till);

        Assert.DoesNotContain(Martin.Id, night.CrewIds);
    }

    // The administrator is never limited by the night, so listing one in its
    // crew would read as if they were.
    [Fact]
    public void Create_WhenCrewIncludesAnAdministrator_ThrowsCrewRoleInvalid()
    {
        StaffUser admin = A(StaffRole.Administrator, "euge");

        DomainException error = Assert.Throws<DomainException>(() => ANight(MainBar, Till, admin));

        Assert.Equal(Night.ErrorCodes.CrewRoleInvalid, error.Code);
    }

    [Fact]
    public void Create_WhenACrewMemberIsInactive_ThrowsCrewMemberInactive()
    {
        StaffUser gone = A(StaffRole.Waiter, "former-waiter");
        gone.Deactivate();

        DomainException error = Assert.Throws<DomainException>(() => ANight(MainBar, Till, gone));

        Assert.Equal(Night.ErrorCodes.CrewMemberInactive, error.Code);
    }

    [Fact]
    public void Create_WhenACrewMemberBelongsToAnotherVenue_ThrowsCrewMemberFromAnotherVenue()
    {
        StaffUser foreign = StaffUser.Create(Guid.CreateVersion7(), "foreign-bar", "hash", StaffRole.Kds);

        DomainException error = Assert.Throws<DomainException>(() => ANight(MainBar, Till, foreign));

        Assert.Equal(Night.ErrorCodes.CrewMemberFromAnotherVenue, error.Code);
    }

    [Fact]
    public void Create_WhenTheSameAccountIsListedTwice_KeepsItOnce()
    {
        Night night = ANight(MainBar, Till, MainBar);

        Assert.Equal([MainBar.Id, Till.Id], night.CrewIds);
    }

    // Criterion 2 and 3: what "the night in progress" means. Closing is
    // exclusive, so a cart confirmed at 06:00 sharp is already too late.
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3 * 60, true)]
    [InlineData(7 * 60 - 1, true)]
    [InlineData(7 * 60, false)]
    public void IsUnderwayAt_ForAMomentAroundTheHours_TellsWhetherItIsOn(int minutesAfterOpening, bool expected)
    {
        Assert.Equal(expected, ANight().IsUnderwayAt(Opening.AddMinutes(minutesAfterOpening)));
    }

    // Criterion 4.
    [Fact]
    public void IsWorkedBy_ForAnAccountInTheCrew_IsTrue()
    {
        Assert.True(ANight(MainBar, Till).IsWorkedBy(MainBar.Id));
    }

    [Fact]
    public void IsWorkedBy_ForAnAccountOutsideTheCrew_IsFalse()
    {
        Assert.False(ANight(MainBar, Till).IsWorkedBy(Martin.Id));
    }

    // The edit rules (US-35, "la noche se extiende"): an upcoming night can
    // change everything, one underway keeps its start, a finished one nothing.
    private static readonly DateTimeOffset BeforeOpening = Opening.AddHours(-2);
    private static readonly DateTimeOffset MidNight = Opening.AddHours(3);

    [Fact]
    public void Update_WhenTheNightHasNotStarted_ChangesTheNameTheHoursAndTheCrew()
    {
        Night night = ANight(MainBar, Till);

        night.Update("Saturday, late", Opening.AddHours(1), Closing.AddHours(1), [MainBar, Till, Martin], BeforeOpening);

        Assert.Equal("Saturday, late", night.Name);
        Assert.Equal(Opening.AddHours(1), night.StartsAt);
        Assert.Equal(Closing.AddHours(1), night.EndsAt);
        Assert.Equal([MainBar.Id, Till.Id, Martin.Id], night.CrewIds);
    }

    // Edits go through the same rules as creating: no KDS, no night.
    [Fact]
    public void Update_WhenTheCrewLosesItsKds_ThrowsKdsRequired()
    {
        Night night = ANight(MainBar, Till);

        DomainException error = Assert.Throws<DomainException>(
            () => night.Update("Saturday", Opening, Closing, [Till], BeforeOpening));

        Assert.Equal(Night.ErrorCodes.KdsRequired, error.Code);
    }

    [Fact]
    public void Update_WhenItDoesNotEndAfterItStarts_ThrowsHoursInvalid()
    {
        Night night = ANight(MainBar, Till);

        DomainException error = Assert.Throws<DomainException>(
            () => night.Update("Saturday", Opening, Opening, [MainBar, Till], BeforeOpening));

        Assert.Equal(Night.ErrorCodes.HoursInvalid, error.Code);
    }

    // "La noche se extiende": the bar is full at 05:30 and stays open.
    [Fact]
    public void Update_WhenTheNightIsUnderway_CanMoveTheEndLater()
    {
        Night night = ANight(MainBar, Till);

        night.Update("Saturday", Opening, Closing.AddHours(1), [MainBar, Till], MidNight);

        Assert.Equal(Closing.AddHours(1), night.EndsAt);
    }

    // Orders already belong to it from its start: moving the start would
    // leave some of them outside their own night.
    [Fact]
    public void Update_WhenTheNightIsUnderwayAndTheStartMoves_ThrowsStartLocked()
    {
        Night night = ANight(MainBar, Till);

        DomainException error = Assert.Throws<DomainException>(
            () => night.Update("Saturday", Opening.AddHours(1), Closing, [MainBar, Till], MidNight));

        Assert.Equal(Night.ErrorCodes.StartLocked, error.Code);
    }

    // "Acortarla a una hora que ya pasó es cerrarla": it ends now, not back
    // then, so no order confirmed meanwhile is left outside its night.
    [Fact]
    public void Update_WhenTheNightIsUnderwayAndTheEndIsAlreadyPast_ClosesItNow()
    {
        Night night = ANight(MainBar, Till);

        night.Update("Saturday", Opening, Opening.AddHours(1), [MainBar, Till], MidNight);

        Assert.Equal(MidNight, night.EndsAt);
        Assert.False(night.IsUnderwayAt(MidNight));
    }

    [Fact]
    public void Update_WhenTheNightIsUnderway_CanChangeTheNameAndTheCrew()
    {
        Night night = ANight(MainBar, Till);

        night.Update("Saturday, packed", Opening, Closing, [MainBar, Till, Martin], MidNight);

        Assert.Equal("Saturday, packed", night.Name);
        Assert.True(night.IsWorkedBy(Martin.Id));
    }

    // Its hours are what the metrics of that night are read against.
    [Fact]
    public void Update_WhenTheNightIsOver_ThrowsNightOver()
    {
        Night night = ANight(MainBar, Till);

        DomainException error = Assert.Throws<DomainException>(
            () => night.Update("Saturday", Opening, Closing, [MainBar, Till], Closing));

        Assert.Equal(Night.ErrorCodes.NightOver, error.Code);
    }
}
