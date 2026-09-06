using DrinkIt.Domain.Common;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Domain.Tests.Staff;

public class StaffUserTests
{
    private static readonly Guid AVenue = Guid.CreateVersion7();

    private static StaffUser AnAdministrator() =>
        StaffUser.Create(AVenue, "euge", "hash", StaffRole.Administrator);

    [Fact]
    public void Create_WhenValid_StartsActive()
    {
        StaffUser user = AnAdministrator();

        Assert.True(user.IsActive);
        Assert.Equal(AVenue, user.VenueId);
        Assert.Equal(StaffRole.Administrator, user.Role);
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    // Staff always belong to a venue. CLAUDE.md: the tenant comes from the token
    // claim, so a user without a venue would make that claim unresolvable.
    [Fact]
    public void Create_WhenVenueIdIsEmpty_ThrowsVenueRequired()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => StaffUser.Create(Guid.Empty, "euge", "hash", StaffRole.Administrator));

        Assert.Equal(StaffUser.ErrorCodes.VenueRequired, error.Code);
    }

    [Theory]
    [InlineData("", StaffUser.ErrorCodes.UsernameRequired)]
    [InlineData("   ", StaffUser.ErrorCodes.UsernameRequired)]
    [InlineData("eu", StaffUser.ErrorCodes.UsernameLength)]
    [InlineData("euge lamela", StaffUser.ErrorCodes.UsernameWhitespace)]
    [InlineData("euge\ttab", StaffUser.ErrorCodes.UsernameWhitespace)]
    public void Create_WhenUsernameIsInvalid_ThrowsWithTheMatchingCode(string username, string expectedCode)
    {
        DomainException error = Assert.Throws<DomainException>(
            () => StaffUser.Create(AVenue, username, "hash", StaffRole.Administrator));

        Assert.Equal(expectedCode, error.Code);
    }

    // Unlike the venue slug, a username is normalised instead of rejected: nobody
    // should fail to log in because they capitalised their own name.
    [Theory]
    [InlineData("Euge", "euge")]
    [InlineData("  EUGE  ", "euge")]
    public void Create_WhenUsernameHasCasingOrPadding_NormalisesIt(string input, string expected)
    {
        StaffUser user = StaffUser.Create(AVenue, input, "hash", StaffRole.Administrator);

        Assert.Equal(expected, user.Username);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenPasswordHashIsBlank_ThrowsPasswordHashRequired(string hash)
    {
        DomainException error = Assert.Throws<DomainException>(
            () => StaffUser.Create(AVenue, "euge", hash, StaffRole.Administrator));

        Assert.Equal(StaffUser.ErrorCodes.PasswordHashRequired, error.Code);
    }

    [Fact]
    public void Create_WhenRoleIsNotADefinedValue_ThrowsRoleInvalid()
    {
        DomainException error = Assert.Throws<DomainException>(
            () => StaffUser.Create(AVenue, "euge", "hash", (StaffRole)99));

        Assert.Equal(StaffUser.ErrorCodes.RoleInvalid, error.Code);
    }

    [Fact]
    public void Deactivate_WhenActive_MakesInactive()
    {
        StaffUser user = AnAdministrator();

        user.Deactivate();

        Assert.False(user.IsActive);
    }

    // The ABM can be double-submitted. Deactivating twice must reach the same
    // state, not fail: the desired outcome already holds.
    [Fact]
    public void Deactivate_WhenAlreadyInactive_IsIdempotent()
    {
        StaffUser user = AnAdministrator();
        user.Deactivate();

        user.Deactivate();

        Assert.False(user.IsActive);
    }

    [Fact]
    public void Activate_WhenInactive_MakesActive()
    {
        StaffUser user = AnAdministrator();
        user.Deactivate();

        user.Activate();

        Assert.True(user.IsActive);
    }
}
