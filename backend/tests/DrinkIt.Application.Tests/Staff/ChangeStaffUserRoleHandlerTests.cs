using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Staff;

/// <summary>
/// US-04, second criterion: a role assigned wrong gets corrected in place,
/// instead of deleting the person and loading them again.
/// </summary>
public class ChangeStaffUserRoleHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static StaffUser AWaiter() => StaffUser.Create(TheVenue, "martin.p", "hash", StaffRole.Waiter);

    private static StaffUser AnAdministrator() =>
        StaffUser.Create(TheVenue, "euge.q", "hash", StaffRole.Administrator);

    [Fact]
    public async Task HandleAsync_WhenTheRoleIsOneTheVenueHandsOut_ReplacesTheOldOne()
    {
        StaffUser martin = AWaiter();
        StaffUsersInMemory staff = new(martin);

        Result<StaffUserSummary> result = await HandlerOver(staff)
            .HandleAsync(new ChangeStaffUserRoleCommand(martin.Id, StaffRole.Administrator), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StaffRole.Administrator, martin.Role);
        Assert.Equal(StaffRole.Administrator, result.Value.Role);
        Assert.Equal(1, staff.Saves);
    }

    // The account is the same one: only what they are allowed to do changes, so
    // they sign in with the password they always had.
    [Fact]
    public async Task HandleAsync_WhenTheRoleChanges_LeavesTheRestOfTheAccountAlone()
    {
        StaffUser martin = AWaiter();

        await HandlerOver(new StaffUsersInMemory(martin))
            .HandleAsync(new ChangeStaffUserRoleCommand(martin.Id, StaffRole.Kds), CancellationToken.None);

        Assert.Equal("martin.p", martin.Username);
        Assert.Equal("hash", martin.PasswordHash);
        Assert.True(martin.IsActive);
    }

    [Fact]
    public async Task HandleAsync_WhenNobodyHereHasThatId_Fails()
    {
        StaffUsersInMemory staff = new(AnAdministrator());

        Result<StaffUserSummary> result = await HandlerOver(staff)
            .HandleAsync(new ChangeStaffUserRoleCommand(Guid.CreateVersion7(), StaffRole.Kds), CancellationToken.None);

        Assert.Equal(StaffUserErrors.NotFound, result.Error);
        Assert.Equal(0, staff.Saves);
    }

    /// <summary>
    /// Demoting the last administrator locks the venue out of its own
    /// administration exactly like deactivating them, so it is the same rule.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenTheyAreTheLastActiveAdministrator_Fails()
    {
        StaffUser euge = AnAdministrator();
        StaffUsersInMemory staff = new(euge, AWaiter());

        Result<StaffUserSummary> result = await HandlerOver(staff)
            .HandleAsync(new ChangeStaffUserRoleCommand(euge.Id, StaffRole.Waiter), CancellationToken.None);

        Assert.Equal(StaffUserErrors.LastAdministrator, result.Error);
        Assert.Equal(StaffRole.Administrator, euge.Role);
        Assert.Equal(0, staff.Saves);
    }

    // Handing the role to somebody else first is the way to step down, and it
    // has to keep working: this rule is about what the venue is left with.
    [Fact]
    public async Task HandleAsync_WhenAnotherAdministratorIsStillActive_LetsThisOneStepDown()
    {
        StaffUser euge = AnAdministrator();
        StaffUser pablo = StaffUser.Create(TheVenue, "pablo.l", "hash", StaffRole.Administrator);
        StaffUsersInMemory staff = new(euge, pablo);

        Result<StaffUserSummary> result = await HandlerOver(staff)
            .HandleAsync(new ChangeStaffUserRoleCommand(euge.Id, StaffRole.Waiter), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StaffRole.Waiter, euge.Role);
    }

    // Saving the same role they already have is not a demotion, so counting is
    // beside the point and it must not be refused.
    [Fact]
    public async Task HandleAsync_WhenTheLastAdministratorKeepsTheirRole_Succeeds()
    {
        StaffUser euge = AnAdministrator();
        StaffUsersInMemory staff = new(euge);

        Result<StaffUserSummary> result = await HandlerOver(staff)
            .HandleAsync(new ChangeStaffUserRoleCommand(euge.Id, StaffRole.Administrator), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StaffRole.Administrator, euge.Role);
    }

    private static ChangeStaffUserRoleHandler HandlerOver(StaffUsersInMemory staff) => new(staff);
}
