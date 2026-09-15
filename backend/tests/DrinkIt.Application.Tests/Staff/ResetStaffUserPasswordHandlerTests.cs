using DrinkIt.Application.Common;
using DrinkIt.Application.Security;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Staff;

/// <summary>
/// US-04, third criterion: somebody forgot their password. Only its hash was
/// ever stored, so nothing can recover it — the administrator sets a new one
/// and hands it over again.
/// </summary>
public class ResetStaffUserPasswordHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    private const string ANewPassword = "a long enough password";

    private static StaffUser AWaiter() =>
        StaffUser.Create(TheVenue, "martin.p", "the-old-hash", StaffRole.Waiter);

    [Fact]
    public async Task HandleAsync_WhenThePasswordIsLongEnough_ReplacesTheStoredHash()
    {
        StaffUser martin = AWaiter();
        StaffUsersInMemory staff = new(martin);

        Result<StaffUserSummary> result = await HandlerOver(staff)
            .HandleAsync(new ResetStaffUserPasswordCommand(martin.Id, ANewPassword), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Fake.HashOf(ANewPassword), martin.PasswordHash);
        Assert.Equal(1, staff.Saves);
    }

    // "Entra con esa y no con la anterior": what makes the old password stop
    // working is that its hash is gone, not that anything else was revoked.
    [Fact]
    public async Task HandleAsync_WhenThePasswordIsReset_LeavesNoTraceOfTheOldOne()
    {
        StaffUser martin = AWaiter();

        await HandlerOver(new StaffUsersInMemory(martin))
            .HandleAsync(new ResetStaffUserPasswordCommand(martin.Id, ANewPassword), CancellationToken.None);

        Assert.NotEqual("the-old-hash", martin.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_WhenThePasswordIsReset_LeavesTheRestOfTheAccountAlone()
    {
        StaffUser martin = AWaiter();

        await HandlerOver(new StaffUsersInMemory(martin))
            .HandleAsync(new ResetStaffUserPasswordCommand(martin.Id, ANewPassword), CancellationToken.None);

        Assert.Equal("martin.p", martin.Username);
        Assert.Equal(StaffRole.Waiter, martin.Role);
        Assert.True(martin.IsActive);
    }

    // The same rule as creating the account, because it is the same decision.
    [Theory]
    [InlineData("")]
    [InlineData("       ")]
    [InlineData("short1")]
    public async Task HandleAsync_WhenThePasswordIsTooShort_FailsWithoutChangingAnything(string password)
    {
        StaffUser martin = AWaiter();
        StaffUsersInMemory staff = new(martin);

        Result<StaffUserSummary> result = await HandlerOver(staff)
            .HandleAsync(new ResetStaffUserPasswordCommand(martin.Id, password), CancellationToken.None);

        Assert.Equal(StaffPasswordPolicy.TooShort, result.Error);
        Assert.Equal("the-old-hash", martin.PasswordHash);
        Assert.Equal(0, staff.Saves);
    }

    [Fact]
    public async Task HandleAsync_WhenNobodyHereHasThatId_Fails()
    {
        StaffUsersInMemory staff = new();

        Result<StaffUserSummary> result = await HandlerOver(staff).HandleAsync(
            new ResetStaffUserPasswordCommand(Guid.CreateVersion7(), ANewPassword), CancellationToken.None);

        Assert.Equal(StaffUserErrors.NotFound, result.Error);
        Assert.Equal(0, staff.Saves);
    }

    // An administrator locked out of their own account is the one person who
    // cannot ask anybody else to fix it, so this one is allowed on purpose.
    [Fact]
    public async Task HandleAsync_WhenTheAdministratorPicksTheirOwnAccount_StillWorks()
    {
        StaffUser euge = StaffUser.Create(TheVenue, "euge.q", "the-old-hash", StaffRole.Administrator);

        Result<StaffUserSummary> result = await HandlerOver(new StaffUsersInMemory(euge))
            .HandleAsync(new ResetStaffUserPasswordCommand(euge.Id, ANewPassword), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Fake.HashOf(ANewPassword), euge.PasswordHash);
    }

    private static ResetStaffUserPasswordHandler HandlerOver(StaffUsersInMemory staff) =>
        new(staff, new Fake.PasswordHasher());

    private static class Fake
    {
        public static string HashOf(string password) => $"hashed:{password}";

        public sealed class PasswordHasher : IPasswordHasher
        {
            public string Hash(string password) => HashOf(password);

            public bool Verify(string password, string hash) => hash == Hash(password);
        }
    }
}
