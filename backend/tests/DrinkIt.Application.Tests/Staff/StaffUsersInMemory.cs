using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Application.Tests.Staff;

/// <summary>
/// The staff repository over a list. Shared by every handler spec here, because
/// four use cases loading the same aggregate would otherwise carry four copies
/// of the same double, and they would drift.
/// </summary>
/// <remarks>
/// It only ever holds one venue's rows. The real one leans on the global query
/// filter for that, so a user from somewhere else simply is not found, and
/// these specs reproduce that by handing back null.
/// </remarks>
internal sealed class StaffUsersInMemory(params StaffUser[] stored) : IStaffUserRepository
{
    private readonly List<StaffUser> _stored = [.. stored];

    /// <summary>How many times the unit of work was committed.</summary>
    public int Saves { get; private set; }

    public StaffUser? Added { get; private set; }

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.Exists(user => user.Username == username));

    public Task AddAsync(StaffUser user, CancellationToken cancellationToken)
    {
        Added = user;
        _stored.Add(user);
        Saves++;

        return Task.CompletedTask;
    }

    public Task<StaffUser?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.Find(user => user.Id == id));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;

        return Task.CompletedTask;
    }

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_stored.Count(user => user is { IsActive: true, Role: StaffRole.Administrator }));
}
