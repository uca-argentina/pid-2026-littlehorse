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
            new(Guid.CreateVersion7(), "euge.q", StaffRole.Administrator, IsActive: true),
            new(Guid.CreateVersion7(), "pablo.l", StaffRole.Waiter, IsActive: false),
        ];

        IResult result = await StaffUsersEndpoints.ListAsync(new Fake.Queries(stored), CancellationToken.None);
        HttpResponseSnapshot response = await EndpointResponse.Execute(result, Path, HttpMethods.Get);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(2, response.Body.GetArrayLength());
        Assert.Equal("euge.q", response.Body[0].GetProperty("username").GetString());
        Assert.Equal("Administrator", response.Body[0].GetProperty("role").GetString());
        Assert.False(response.Body[1].GetProperty("isActive").GetBoolean());
    }

    private static async Task<HttpResponseSnapshot> Create(
        string username,
        string password,
        string role,
        string? taken = null)
    {
        CreateStaffUserHandler handler = new(
            new Fake.Repository(taken),
            new Fake.PasswordHasher(),
            new Fake.CurrentVenue());

        IResult result = await StaffUsersEndpoints.CreateAsync(
            new CreateStaffUserRequest(username, password, role), handler, CancellationToken.None);

        return await EndpointResponse.Execute(result, Path, HttpMethods.Post);
    }

    private static class Fake
    {
        public sealed class CurrentVenue : ICurrentVenue
        {
            public Guid Id { get; } = Guid.CreateVersion7();
        }

        public sealed class Repository(string? taken) : IStaffUserRepository
        {
            public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) =>
                Task.FromResult(username == taken);

            public Task AddAsync(StaffUser user, CancellationToken cancellationToken) => Task.CompletedTask;
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
