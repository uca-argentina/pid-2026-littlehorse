using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Staff;

/// <summary>
/// US-05. Somebody who stops working here loses access without losing their
/// row: the orders they prepared keep pointing at their account.
/// </summary>
public class DeactivateStaffUserHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static StaffUser AWaiter() => StaffUser.Create(TheVenue, "martin.p", "hash", StaffRole.Waiter);

    private static StaffUser AnAdministrator() =>
        StaffUser.Create(TheVenue, "euge.q", "hash", StaffRole.Administrator);

    [Fact]
    public async Task HandleAsync_WhenTheyWorkHere_LeavesThemUnableToSignIn()
    {
        StaffUser martin = AWaiter();
        StaffUsersInMemory staff = new(martin, AnAdministrator());

        Result<StaffUserSummary> result = await HandlerOver(staff).HandleAsync(martin.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(martin.IsActive);
        Assert.False(result.Value.IsActive);
    }

    // The point of the whole story: a soft delete, so the row survives. Nothing
    // else in the account changes either, because the person may come back.
    [Fact]
    public async Task HandleAsync_WhenTheyWorkHere_KeepsEverythingElseAboutTheAccount()
    {
        StaffUser martin = AWaiter();
        StaffUsersInMemory staff = new(martin);

        await HandlerOver(staff).HandleAsync(martin.Id, CancellationToken.None);

        Assert.Equal("martin.p", martin.Username);
        Assert.Equal("hash", martin.PasswordHash);
        Assert.Equal(StaffRole.Waiter, martin.Role);
        Assert.NotNull(await staff.GetForUpdateAsync(martin.Id, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WhenTheyWorkHere_CommitsTheChange()
    {
        StaffUser martin = AWaiter();
        StaffUsersInMemory staff = new(martin);

        await HandlerOver(staff).HandleAsync(martin.Id, CancellationToken.None);

        Assert.Equal(1, staff.Saves);
    }

    // Either they never existed or they belong to another venue, and the global
    // query filter makes those the same thing. Saying which would let an
    // administrator probe for ids that exist elsewhere.
    [Fact]
    public async Task HandleAsync_WhenNobodyHereHasThatId_Fails()
    {
        StaffUsersInMemory staff = new(AnAdministrator());

        Result<StaffUserSummary> result = await HandlerOver(staff).HandleAsync(
            Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(StaffUserErrors.NotFound, result.Error);
        Assert.Equal(0, staff.Saves);
    }

    /// <summary>
    /// The venue must never be left unable to administer itself. Nothing can
    /// create or promote a staff user without an administrator token, so a
    /// venue that loses its last one has no way back in at all.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheyAreTheLastActiveAdministrator_Fails()
    {
        StaffUser euge = AnAdministrator();
        StaffUsersInMemory staff = new(euge, AWaiter());

        Result<StaffUserSummary> result = await HandlerOver(staff).HandleAsync(euge.Id, CancellationToken.None);

        Assert.Equal(StaffUserErrors.LastAdministrator, result.Error);
        Assert.True(euge.IsActive);
        Assert.Equal(0, staff.Saves);
    }

    /// <summary>
    /// Two administrators deactivating each other is what a rule about "your own
    /// account" does not stop: the role and the active flag travel in a token
    /// that lasts eight hours and is never re-checked, so somebody already
    /// deactivated can still spend theirs. Counting what would be left does stop
    /// it, whoever is asking and however stale their token is.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenAnotherAdministratorWasJustDeactivated_RefusesToTakeTheLastOne()
    {
        StaffUser euge = AnAdministrator();
        StaffUser pablo = StaffUser.Create(TheVenue, "pablo.l", "hash", StaffRole.Administrator);
        pablo.Deactivate();
        StaffUsersInMemory staff = new(euge, pablo);

        Result<StaffUserSummary> result = await HandlerOver(staff).HandleAsync(euge.Id, CancellationToken.None);

        Assert.Equal(StaffUserErrors.LastAdministrator, result.Error);
        Assert.True(euge.IsActive);
    }

    // Stepping down is allowed while somebody else can still administer the
    // venue. The rule protects the venue, not anybody's own account.
    [Fact]
    public async Task HandleAsync_WhenAnotherAdministratorIsStillActive_LetsThisOneGo()
    {
        StaffUser euge = AnAdministrator();
        StaffUser pablo = StaffUser.Create(TheVenue, "pablo.l", "hash", StaffRole.Administrator);
        StaffUsersInMemory staff = new(euge, pablo);

        Result<StaffUserSummary> result = await HandlerOver(staff).HandleAsync(euge.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(euge.IsActive);
    }

    // The button can be double-tapped on a slow connection, and the outcome the
    // administrator asked for already holds.
    [Fact]
    public async Task HandleAsync_WhenTheyAreAlreadyInactive_Succeeds()
    {
        StaffUser martin = AWaiter();
        martin.Deactivate();
        StaffUsersInMemory staff = new(martin);

        Result<StaffUserSummary> result = await HandlerOver(staff).HandleAsync(martin.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(martin.IsActive);
    }

    private static DeactivateStaffUserHandler HandlerOver(StaffUsersInMemory staff) => new(staff);
}
