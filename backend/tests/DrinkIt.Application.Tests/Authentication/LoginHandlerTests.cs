using DrinkIt.Application.Authentication;
using DrinkIt.Application.Common;
using DrinkIt.Application.Security;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Authentication;

public class LoginHandlerTests
{
    private const string RightPassword = "the right password";

    private static readonly StaffCredentials ActiveAdministrator = new(
        StaffUserId: Guid.CreateVersion7(),
        VenueId: Guid.CreateVersion7(),
        Username: "euge",
        PasswordHash: Fake.HashOf(RightPassword),
        Role: StaffRole.Administrator,
        IsActive: true);

    [Fact]
    public async Task HandleAsync_WhenCredentialsAreValid_ReturnsTheStaffUser()
    {
        LoginHandler handler = HandlerFinding(ActiveAdministrator);

        Result<LoggedInStaff> result = await handler.HandleAsync(
            new LoginCommand("euge", RightPassword), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ActiveAdministrator.StaffUserId, result.Value.StaffUserId);
        Assert.Equal(ActiveAdministrator.VenueId, result.Value.VenueId);
        Assert.Equal(StaffRole.Administrator, result.Value.Role);
    }

    [Fact]
    public async Task HandleAsync_WhenTheUserDoesNotExist_Fails()
    {
        LoginHandler handler = HandlerFinding(null);

        Result<LoggedInStaff> result = await handler.HandleAsync(
            new LoginCommand("nobody", RightPassword), CancellationToken.None);

        Assert.Equal(LoginHandler.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheUserIsDeactivated_Fails()
    {
        LoginHandler handler = HandlerFinding(ActiveAdministrator with { IsActive = false });

        Result<LoggedInStaff> result = await handler.HandleAsync(
            new LoginCommand("euge", RightPassword), CancellationToken.None);

        Assert.Equal(LoginHandler.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenThePasswordIsWrong_Fails()
    {
        LoginHandler handler = HandlerFinding(ActiveAdministrator);

        Result<LoggedInStaff> result = await handler.HandleAsync(
            new LoginCommand("euge", "not the right password"), CancellationToken.None);

        Assert.Equal(LoginHandler.InvalidCredentials, result.Error);
    }

    // Timing mitigation. If the handler returned early for an unknown username it
    // would answer in microseconds, while a real username costs ~100 ms of key
    // derivation. That difference alone lets an attacker enumerate valid users.
    [Fact]
    public async Task HandleAsync_WhenTheUserDoesNotExist_StillSpendsTimeHashing()
    {
        Fake.PasswordHasher hasher = new();
        LoginHandler handler = new(new Fake.CredentialsQuery(null), hasher);

        await handler.HandleAsync(
            new LoginCommand("nobody", RightPassword), CancellationToken.None);

        Assert.True(hasher.WasUsed, "the handler returned without running the key derivation");
    }

    private static LoginHandler HandlerFinding(StaffCredentials? found) =>
        new(new Fake.CredentialsQuery(found), new Fake.PasswordHasher());

    private static class Fake
    {
        public static string HashOf(string password) => $"hashed:{password}";

        public sealed class CredentialsQuery(StaffCredentials? found) : IStaffCredentialsQuery
        {
            public Task<StaffCredentials?> FindAsync(string username, CancellationToken cancellationToken) =>
                Task.FromResult(found);
        }

        public sealed class PasswordHasher : IPasswordHasher
        {
            public bool WasUsed { get; private set; }

            public string Hash(string password)
            {
                WasUsed = true;
                return HashOf(password);
            }

            public bool Verify(string password, string hash)
            {
                WasUsed = true;
                return hash == HashOf(password);
            }
        }
    }
}
