using DrinkIt.Application.Staff;
using DrinkIt.Domain.Staff;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Staff;

internal sealed class StaffUserRepository(DrinkItDbContext context) : IStaffUserRepository
{
    /// <summary>
    /// No venue in the WHERE: the global query filter adds the one the request
    /// resolved. Writing it by hand is what would let one venue's check read
    /// another venue's rows.
    /// </summary>
    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) =>
        context.StaffUsers.AnyAsync(user => user.Username == username, cancellationToken);

    public async Task AddAsync(StaffUser user, CancellationToken cancellationToken)
    {
        context.StaffUsers.Add(user);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Tracked, unlike every read on this side of the app: the use case is
    /// about to change the aggregate. No venue in the WHERE either, so a user
    /// from another venue is simply not found.
    /// </summary>
    public Task<StaffUser?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        context.StaffUsers.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Counted inside this venue, like everything else here: the global query
    /// filter scopes it, so another venue's administrators never make this one
    /// look safe to empty.
    /// </summary>
    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        context.StaffUsers.CountAsync(
            user => user.IsActive && user.Role == StaffRole.Administrator,
            cancellationToken);
}
