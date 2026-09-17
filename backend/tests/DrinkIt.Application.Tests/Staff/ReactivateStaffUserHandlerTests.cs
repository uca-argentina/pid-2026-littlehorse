using DrinkIt.Application.Common;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Staff;

/// <summary>
/// US-05, fourth criterion: somebody who comes back to work gets their own
/// account again, rather than a second one loaded from scratch.
/// </summary>
public class ReactivateStaffUserHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private static StaffUser SomebodyWhoLeft()
    {
        StaffUser user = StaffUser.Create(TheVenue, "martin.p", "hash", StaffRole.Waiter);
        user.Deactivate();

        return user;
    }

    [Fact]
    public async Task HandleAsync_WhenTheyCameBack_LetsThemSignInAgain()
    {
        StaffUser martin = SomebodyWhoLeft();
        StaffUsersInMemory staff = new(martin);

        Result<StaffUserSummary> result = await new ReactivateStaffUserHandler(staff)
            .HandleAsync(martin.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(martin.IsActive);
        Assert.True(result.Value.IsActive);
    }

    // "Sin cargarla de nuevo": the same username, the same password they always
    // had, the same role. Reactivating is not a second hiring.
    [Fact]
    public async Task HandleAsync_WhenTheyCameBack_GivesThemTheAccountTheyAlwaysHad()
    {
        StaffUser martin = SomebodyWhoLeft();

        await new ReactivateStaffUserHandler(new StaffUsersInMemory(martin))
            .HandleAsync(martin.Id, CancellationToken.None);

        Assert.Equal("martin.p", martin.Username);
        Assert.Equal("hash", martin.PasswordHash);
        Assert.Equal(StaffRole.Waiter, martin.Role);
    }

    [Fact]
    public async Task HandleAsync_WhenTheyCameBack_CommitsTheChange()
    {
        StaffUser martin = SomebodyWhoLeft();
        StaffUsersInMemory staff = new(martin);

        await new ReactivateStaffUserHandler(staff).HandleAsync(martin.Id, CancellationToken.None);

        Assert.Equal(1, staff.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenNobodyHereHasThatId_Fails()
    {
        StaffUsersInMemory staff = new();

        Result<StaffUserSummary> result = await new ReactivateStaffUserHandler(staff)
            .HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(StaffUserErrors.NotFound, result.Error);
        Assert.Equal(0, staff.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenTheyWereNeverDeactivated_Succeeds()
    {
        StaffUser martin = StaffUser.Create(TheVenue, "martin.p", "hash", StaffRole.Waiter);

        Result<StaffUserSummary> result = await new ReactivateStaffUserHandler(new StaffUsersInMemory(martin))
            .HandleAsync(martin.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(martin.IsActive);
    }
}
