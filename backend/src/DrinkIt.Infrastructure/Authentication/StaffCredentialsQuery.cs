using DrinkIt.Application.Authentication;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Authentication;

internal sealed class StaffCredentialsQuery(DrinkItDbContext context) : IStaffCredentialsQuery
{
    /// <summary>
    /// No venue in the WHERE on purpose: the global query filter adds it from
    /// the venue the middleware already resolved. Writing it by hand here, or
    /// reaching for IgnoreQueryFilters, is what would let one venue read
    /// another one's staff.
    /// </summary>
    public Task<StaffCredentials?> FindAsync(string username, CancellationToken cancellationToken) =>
        context.StaffUsers
            .AsNoTracking()
            .Where(user => user.Username == username)
            .Select(user => new StaffCredentials(
                user.Id,
                user.VenueId,
                user.Username,
                user.PasswordHash,
                user.Role,
                user.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
}
