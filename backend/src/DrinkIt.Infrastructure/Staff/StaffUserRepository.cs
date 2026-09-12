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
}
