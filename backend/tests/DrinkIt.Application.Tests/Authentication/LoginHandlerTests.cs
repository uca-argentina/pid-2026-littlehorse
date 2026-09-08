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
    public async Task HandleAsync_WhenCredentialsAreValid_ReturnsASessionForThatUser()
    {
        LoginHandler handler = HandlerFinding(ActiveAdministrator);

        Result<StaffSession> result = await handler.HandleAsync(
            new LoginCommand("euge", RightPassword), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Fake.TokenFor(ActiveAdministrator.StaffUserId), result.Value.Token);
        Assert.Equal("euge", result.Value.Username);
        Assert.Equal(StaffRole.Administrator, result.Value.Role);
    }

    // The venue has to reach the token: from here on it is the only source of
    // the tenant, and the URL stops being trusted.
    [Fact]
    public async Task HandleAsync_WhenCredentialsAreValid_IssuesTheTokenForTheUserVenue()
    {
        Fake.TokenIssuer issuer = new();
        LoginHandler handler = new(new Fake.CredentialsQuery(ActiveAdministrator), new Fake.PasswordHasher(), issuer);

        await handler.HandleAsync(new LoginCommand("euge", RightPassword), CancellationToken.None);

        Assert.Equal(ActiveAdministrator.VenueId, issuer.IssuedForVenue);
        Assert.Equal(ActiveAdministrator.StaffUserId, issuer.IssuedForStaffUser);
        Assert.Equal(StaffRole.Administrator, issuer.IssuedForRole);
    }

    [Fact]
    public async Task HandleAsync_WhenTheUserDoesNotExist_Fails()
    {
        LoginHandler handler = HandlerFinding(null);

        Result<StaffSession> result = await handler.HandleAsync(
            new LoginCommand("nobody", RightPassword), CancellationToken.None);

        Assert.Equal(LoginHandler.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenTheUserIsDeactivated_Fails()
    {
        LoginHandler handler = HandlerFinding(ActiveAdministrator with { IsActive = false });

        Result<StaffSession> result = await handler.HandleAsync(
            new LoginCommand("euge", RightPassword), CancellationToken.None);

        Assert.Equal(LoginHandler.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task HandleAsync_WhenThePasswordIsWrong_Fails()
    {
        LoginHandler handler = HandlerFinding(ActiveAdministrator);

        Result<StaffSession> result = await handler.HandleAsync(
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
        LoginHandler handler = new(new Fake.CredentialsQuery(null), hasher, new Fake.TokenIssuer());

        await handler.HandleAsync(
            new LoginCommand("nobody", RightPassword), CancellationToken.None);

        Assert.True(hasher.WasUsed, "the handler returned without running the key derivation");
    }

    // The stored username is lowercase. Typing it with capitals must still work,
    // and that must not depend on how the database collates strings.
    [Theory]
    [InlineData("EUGE")]
    [InlineData("  Euge  ")]
    public async Task HandleAsync_WhenTheUsernameIsTypedWithCapitals_StillFindsTheUser(string typed)
    {
        LoginHandler handler = HandlerFinding(ActiveAdministrator);

        Result<StaffSession> result = await handler.HandleAsync(
            new LoginCommand(typed, RightPassword), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static LoginHandler HandlerFinding(StaffCredentials? found) =>
        new(new Fake.CredentialsQuery(found), new Fake.PasswordHasher(), new Fake.TokenIssuer());

    private static class Fake
    {
        public static string HashOf(string password) => $"hashed:{password}";

        public static string TokenFor(Guid staffUserId) => $"token:{staffUserId}";

        public sealed class TokenIssuer : ITokenIssuer
        {
            public Guid IssuedForStaffUser { get; private set; }

            public Guid IssuedForVenue { get; private set; }

            public StaffRole IssuedForRole { get; private set; }

            public AccessToken Issue(Guid staffUserId, Guid venueId, string username, StaffRole role)
            {
                IssuedForStaffUser = staffUserId;
                IssuedForVenue = venueId;
                IssuedForRole = role;

                return new AccessToken(TokenFor(staffUserId), DateTimeOffset.UtcNow.AddHours(1));
            }
        }

        public sealed class CredentialsQuery(StaffCredentials? found) : IStaffCredentialsQuery
        {
            // Only answers to the exact stored form, so the test fails if the
            // handler forwards the username without normalising it.
            public Task<StaffCredentials?> FindAsync(string username, CancellationToken cancellationToken) =>
                Task.FromResult(username == found?.Username ? found : null);
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
