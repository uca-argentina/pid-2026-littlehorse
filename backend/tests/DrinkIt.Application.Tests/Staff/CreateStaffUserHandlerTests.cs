using DrinkIt.Application.Common;
using DrinkIt.Application.Security;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Staff;

public class CreateStaffUserHandlerTests
{
    private static readonly Guid TheVenue = Guid.CreateVersion7();

    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_AddsTheUserToTheVenueOfTheSignedInAdministrator()
    {
        Fake.StaffUsers staff = new();
        CreateStaffUserHandler handler = HandlerOver(staff);

        Result<CreatedStaffUser> result = await handler.HandleAsync(
            new CreateStaffUserCommand("martin.p", "a long enough password", StaffRole.Waiter),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TheVenue, staff.Added!.VenueId);
        Assert.Equal("martin.p", staff.Added.Username);
        Assert.Equal(StaffRole.Waiter, staff.Added.Role);
    }

    // "Puede iniciar sesión enseguida": login rejects an inactive user, so a new
    // one that did not start active would be created and unusable.
    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_LeavesTheUserAbleToSignIn()
    {
        Fake.StaffUsers staff = new();

        await HandlerOver(staff).HandleAsync(
            new CreateStaffUserCommand("martin.p", "a long enough password", StaffRole.Kds),
            CancellationToken.None);

        Assert.True(staff.Added!.IsActive);
    }

    // What is stored is whatever the hasher returned, never the typed password.
    // The hasher is a port here; the real derivation is covered in
    // DrinkIt.Infrastructure.Tests.
    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_StoresThePasswordAsTheHasherReturnedIt()
    {
        Fake.StaffUsers staff = new();

        await HandlerOver(staff).HandleAsync(
            new CreateStaffUserCommand("martin.p", "a long enough password", StaffRole.Kds),
            CancellationToken.None);

        Assert.Equal(Fake.HashOf("a long enough password"), staff.Added!.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_WhenTheUsernameIsAlreadyUsedInThisVenue_Fails()
    {
        Fake.StaffUsers staff = new(taken: "martin.p");

        Result<CreatedStaffUser> result = await HandlerOver(staff).HandleAsync(
            new CreateStaffUserCommand("martin.p", "a long enough password", StaffRole.Waiter),
            CancellationToken.None);

        Assert.Equal(CreateStaffUserHandler.UsernameTaken, result.Error);
        Assert.Null(staff.Added);
    }

    // The stored username is lowercase, so availability has to be checked in that
    // same form. Otherwise "Martin.P" slips past the check and the unique index
    // rejects it later as a 500 instead of a message the administrator can read.
    [Fact]
    public async Task HandleAsync_WhenTheTakenUsernameIsTypedWithCapitals_StillFails()
    {
        Fake.StaffUsers staff = new(taken: "martin.p");

        Result<CreatedStaffUser> result = await HandlerOver(staff).HandleAsync(
            new CreateStaffUserCommand("  Martin.P  ", "a long enough password", StaffRole.Waiter),
            CancellationToken.None);

        Assert.Equal(CreateStaffUserHandler.UsernameTaken, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("       ")]
    [InlineData("short1")]
    public async Task HandleAsync_WhenThePasswordIsTooShort_FailsWithoutCreatingAnything(string password)
    {
        Fake.StaffUsers staff = new();

        Result<CreatedStaffUser> result = await HandlerOver(staff).HandleAsync(
            new CreateStaffUserCommand("martin.p", password, StaffRole.Waiter),
            CancellationToken.None);

        Assert.Equal(CreateStaffUserHandler.PasswordTooShort, result.Error);
        Assert.Null(staff.Added);
    }

    // What the screen puts in the list right after saving. The password never
    // travels back, not even hashed.
    [Fact]
    public async Task HandleAsync_WhenTheDataIsValid_ReturnsWhatTheListingShows()
    {
        Fake.StaffUsers staff = new();

        Result<CreatedStaffUser> result = await HandlerOver(staff).HandleAsync(
            new CreateStaffUserCommand("Martin.P", "a long enough password", StaffRole.Waiter),
            CancellationToken.None);

        Assert.Equal(staff.Added!.Id, result.Value.Id);
        Assert.Equal("martin.p", result.Value.Username);
        Assert.Equal(StaffRole.Waiter, result.Value.Role);
        Assert.True(result.Value.IsActive);
    }

    private static CreateStaffUserHandler HandlerOver(Fake.StaffUsers staff) =>
        new(staff, new Fake.PasswordHasher(), new Fake.CurrentVenue());

    private static class Fake
    {
        public static string HashOf(string password) => $"hashed:{password}";

        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id => TheVenue;
        }

        public sealed class StaffUsers(string? taken = null) : IStaffUserRepository
        {
            public StaffUser? Added { get; private set; }

            public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) =>
                Task.FromResult(username == taken);

            public Task AddAsync(StaffUser user, CancellationToken cancellationToken)
            {
                Added = user;

                return Task.CompletedTask;
            }
        }

        public sealed class PasswordHasher : IPasswordHasher
        {
            public string Hash(string password) => HashOf(password);

            public bool Verify(string password, string hash) => hash == Hash(password);
        }
    }
}
