using DrinkIt.Api.Features.Staff;
using DrinkIt.Api.Tests.Common;
using DrinkIt.Application.Common;
using DrinkIt.Application.Security;
using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;
using Microsoft.AspNetCore.Http;

namespace DrinkIt.Api.Tests.Features.Staff;

public class StaffUsersEndpointsTests
{
    private const string Path = "/staff/users";

    private static readonly DateTimeOffset Created = new(2026, 9, 27, 21, 0, 0, TimeSpan.Zero);

    private const string LongEnoughPassword = "a long enough password";

    // Pins the success shape: the Angular client is generated from it, so
    // renaming a property here breaks the screen silently.
    [Fact]
    public async Task CreateAsync_WhenTheDataIsValid_RespondsWithTheCreatedUser()
    {
        HttpResponseSnapshot response = await Create("Martin.P", LongEnoughPassword, "Waiter");

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.StartsWith("application/json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("martin.p", response.Text("username"));
        Assert.Equal("Waiter", response.Text("role"));
        Assert.True(response.Body.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(Guid.Empty, response.Body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CreateAsync_WhenTheUsernameIsAlreadyUsedInThisVenue_RespondsWithConflict()
    {
        HttpResponseSnapshot response = await Create("martin.p", LongEnoughPassword, "Waiter", taken: "martin.p");

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:drinkit:problem:staff:username-taken", response.Text("type"));
    }

    [Fact]
    public async Task CreateAsync_WhenThePasswordIsTooShort_RespondsWithBadRequest()
    {
        HttpResponseSnapshot response = await Create("martin.p", "short", "Waiter");

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:staff:password-too-short", response.Text("type"));
    }

    /// <summary>
    /// The wire carries the role as a name, so anything that is not one of ours
    /// is malformed input and comes back as a 400 rather than as a crash.
    /// </summary>
    /// <remarks>
    /// The comma case is the one that surprises: Enum.TryParse ORs a
    /// comma-separated list even for an enum that is not [Flags], so
    /// "Administrator,Kds" parses as 1|2 and lands on Waiter. A request nobody
    /// would write by hand, and silently the wrong role if it ever arrives.
    /// </remarks>
    [Theory]
    [InlineData("Cashier")]
    [InlineData("")]
    [InlineData("99")]
    [InlineData("Administrator,Kds")]
    [InlineData("Waiter, Administrator")]
    public async Task CreateAsync_WhenTheRoleIsNotOneAVenueHandsOut_RespondsWithBadRequest(string role)
    {
        HttpResponseSnapshot response = await Create("martin.p", LongEnoughPassword, role);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:staff:role-invalid", response.Text("type"));
    }

    // Written lowercase by a phone keyboard that capitalises, or by hand. The
    // role picker sends a fixed value, but nothing on the wire guarantees it.
    [Fact]
    public async Task CreateAsync_WhenTheRoleIsTypedWithOtherCasing_StillCreatesTheUser()
    {
        HttpResponseSnapshot response = await Create("martin.p", LongEnoughPassword, "kds");

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Equal("Kds", response.Text("role"));
    }

    [Fact]
    public async Task ListAsync_WhenTheVenueHasStaff_IncludesTheDeactivatedOnes()
    {
        StaffUserListItem[] stored =
        [
            new(Guid.CreateVersion7(), "euge.q", StaffRole.Administrator, IsActive: true, new AuditInfo(Created, "euge.q", null, null)),
            new(Guid.CreateVersion7(), "pablo.l", StaffRole.Waiter, IsActive: false, new AuditInfo(null, null, null, null)),
        ];

        IResult result = await StaffUsersEndpoints.ListAsync(new Fake.Queries(stored), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(2, response.Body.GetArrayLength());
        Assert.Equal("euge.q", response.Body[0].GetProperty("username").GetString());
        Assert.Equal("Administrator", response.Body[0].GetProperty("role").GetString());
        Assert.False(response.Body[1].GetProperty("isActive").GetBoolean());
    }

    // US-30. The shape the screen reads, and the row from before the columns
    // existed, which has to arrive as nulls and not as a date.
    [Fact]
    public async Task ListAsync_WhenTheRowsCarryAnAudit_RespondsWithItAndWithNullsWhereThereIsNone()
    {
        StaffUserListItem[] stored =
        [
            new(Guid.CreateVersion7(), "euge.q", StaffRole.Administrator, IsActive: true, new AuditInfo(Created, "root", Created.AddHours(1), "pablo.l")),
            new(Guid.CreateVersion7(), "viejo.v", StaffRole.Waiter, IsActive: true, new AuditInfo(null, null, null, null)),
        ];

        IResult result = await StaffUsersEndpoints.ListAsync(new Fake.Queries(stored), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        System.Text.Json.JsonElement audit = response.Body[0].GetProperty("audit");
        Assert.Equal(Created, audit.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal("root", audit.GetProperty("createdBy").GetString());
        Assert.Equal(Created.AddHours(1), audit.GetProperty("lastModifiedAt").GetDateTimeOffset());
        Assert.Equal("pablo.l", audit.GetProperty("lastModifiedBy").GetString());

        System.Text.Json.JsonElement legacy = response.Body[1].GetProperty("audit");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, legacy.GetProperty("createdAt").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, legacy.GetProperty("createdBy").ValueKind);
    }

    private static async Task<HttpResponseSnapshot> Create(
        string username,
        string password,
        string role,
        string? taken = null)
    {
        Fake.Repository staff = taken is null
            ? new Fake.Repository()
            : new Fake.Repository(StaffUser.Create(Guid.CreateVersion7(), taken, "hash", StaffRole.Waiter));

        CreateStaffUserHandler handler = new(staff, new Fake.PasswordHasher(), new Fake.CurrentVenue());

        IResult result = await StaffUsersEndpoints.CreateAsync(
            new CreateStaffUserRequest(username, password, role), handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Post);
    }

    // US-04, second criterion. The account is the same one; only what they are
    // allowed to do changes.
    [Fact]
    public async Task ChangeRoleAsync_WhenTheRoleIsOneTheVenueHandsOut_RespondsWithTheUpdatedUser()
    {
        StaffUser martin = AWaiter();
        Fake.Repository staff = new(martin);

        IResult result = await StaffUsersEndpoints.ChangeRoleAsync(
            martin.Id,
            new ChangeStaffUserRoleRequest("Kds"),
            new ChangeStaffUserRoleHandler(staff),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Put);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("Kds", response.Text("role"));
        Assert.Equal("martin.p", response.Text("username"));
    }

    [Fact]
    public async Task ChangeRoleAsync_WhenTheRoleIsNotOneAVenueHandsOut_RespondsWithBadRequest()
    {
        StaffUser martin = AWaiter();

        IResult result = await StaffUsersEndpoints.ChangeRoleAsync(
            martin.Id,
            new ChangeStaffUserRoleRequest("Cashier"),
            new ChangeStaffUserRoleHandler(new Fake.Repository(martin)),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Put);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:staff:role-invalid", response.Text("type"));
    }

    // Either nobody has that id or they belong to another venue, and the query
    // filter makes those the same answer on purpose.
    [Fact]
    public async Task ChangeRoleAsync_WhenNobodyHereHasThatId_RespondsWithNotFound()
    {
        IResult result = await StaffUsersEndpoints.ChangeRoleAsync(
            Guid.CreateVersion7(),
            new ChangeStaffUserRoleRequest("Kds"),
            new ChangeStaffUserRoleHandler(new Fake.Repository()),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Put);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:staff:not-found", response.Text("type"));
    }

    // US-04, third criterion.
    [Fact]
    public async Task ResetPasswordAsync_WhenThePasswordIsLongEnough_RespondsWithTheUpdatedUser()
    {
        StaffUser martin = AWaiter();

        IResult result = await StaffUsersEndpoints.ResetPasswordAsync(
            martin.Id,
            new ResetStaffUserPasswordRequest(LongEnoughPassword),
            new ResetStaffUserPasswordHandler(new Fake.Repository(martin), new Fake.PasswordHasher()),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Put);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("martin.p", response.Text("username"));
    }

    // The new password never travels back, not even hashed: the administrator
    // already knows it, and nothing else has any business reading it.
    [Fact]
    public async Task ResetPasswordAsync_WhenThePasswordIsLongEnough_SendsNoPasswordBack()
    {
        StaffUser martin = AWaiter();

        IResult result = await StaffUsersEndpoints.ResetPasswordAsync(
            martin.Id,
            new ResetStaffUserPasswordRequest(LongEnoughPassword),
            new ResetStaffUserPasswordHandler(new Fake.Repository(martin), new Fake.PasswordHasher()),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Put);

        Assert.DoesNotContain(LongEnoughPassword, response.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("password", response.Raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenThePasswordIsTooShort_RespondsWithBadRequest()
    {
        StaffUser martin = AWaiter();

        IResult result = await StaffUsersEndpoints.ResetPasswordAsync(
            martin.Id,
            new ResetStaffUserPasswordRequest("short"),
            new ResetStaffUserPasswordHandler(new Fake.Repository(martin), new Fake.PasswordHasher()),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Put);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:staff:password-too-short", response.Text("type"));
    }

    // US-05, first and second criteria: no access left, and the row still there.
    [Fact]
    public async Task DeactivateAsync_WhenTheyWorkHere_RespondsWithTheUserMarkedInactive()
    {
        StaffUser martin = AWaiter();

        IResult result = await StaffUsersEndpoints.DeactivateAsync(
            martin.Id,
            new DeactivateStaffUserHandler(new Fake.Repository(martin)),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.False(response.Body.GetProperty("isActive").GetBoolean());
        Assert.Equal("martin.p", response.Text("username"));
    }

    // The venue must never be left with nobody who can administer it.
    [Fact]
    public async Task DeactivateAsync_WhenTheyAreTheLastActiveAdministrator_RespondsWithConflict()
    {
        StaffUser euge = StaffUser.Create(Guid.CreateVersion7(), "euge.q", "hash", StaffRole.Administrator);

        IResult result = await StaffUsersEndpoints.DeactivateAsync(
            euge.Id,
            new DeactivateStaffUserHandler(new Fake.Repository(euge)),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("urn:drinkit:problem:staff:last-administrator", response.Text("type"));
    }

    // US-05, fourth criterion.
    [Fact]
    public async Task ReactivateAsync_WhenTheyCameBack_RespondsWithTheUserMarkedActive()
    {
        StaffUser martin = AWaiter();
        martin.Deactivate();

        IResult result = await StaffUsersEndpoints.ReactivateAsync(
            martin.Id,
            new ReactivateStaffUserHandler(new Fake.Repository(martin)),
            CancellationToken.None);

        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Post);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.True(response.Body.GetProperty("isActive").GetBoolean());
        Assert.Equal("martin.p", response.Text("username"));
    }

    private static StaffUser AWaiter() =>
        StaffUser.Create(Guid.CreateVersion7(), "martin.p", "hash", StaffRole.Waiter);

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id { get; } = Guid.CreateVersion7();
        }

        /// <summary>The staff repository over a list, shared by every case here.</summary>
        public sealed class Repository(params StaffUser[] stored) : IStaffUserRepository
        {
            private readonly List<StaffUser> _stored = [.. stored];

            public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) =>
                Task.FromResult(_stored.Exists(user => user.Username == username));

            public Task AddAsync(StaffUser user, CancellationToken cancellationToken)
            {
                _stored.Add(user);

                return Task.CompletedTask;
            }

            public Task<StaffUser?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
                Task.FromResult(_stored.Find(user => user.Id == id));

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
                Task.FromResult(_stored.Count(user => user is { IsActive: true, Role: StaffRole.Administrator }));
        }

        public sealed class Queries(StaffUserListItem[] stored) : IStaffUserQueries
        {
            public Task<IReadOnlyList<StaffUserListItem>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<StaffUserListItem>>(stored);
        }

        public sealed class PasswordHasher : IPasswordHasher
        {
            public string Hash(string password) => $"hashed:{password}";

            public bool Verify(string password, string hash) => hash == Hash(password);
        }
    }
}
