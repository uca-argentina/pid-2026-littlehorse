using DrinkIt.Application.Staff;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.Staff;

/// <summary>
/// Read side: no repository, no tracking, and a projection straight to the DTO
/// the screen shows. CLAUDE.md, persistence section.
/// </summary>
internal sealed class StaffUserQueries(DrinkItDbContext context) : IStaffUserQueries
{
    public async Task<IReadOnlyList<StaffUserListItem>> ListAsync(CancellationToken cancellationToken) =>
        await context.StaffUsers
            .AsNoTracking()
            // Whoever still works here comes first: the deactivated rows are
            // kept for the order history, not to be read every shift.
            .OrderByDescending(user => user.IsActive)
            .ThenBy(user => user.Username)
            .Select(user => new StaffUserListItem(user.Id, user.Username, user.Role, user.IsActive))
            .ToListAsync(cancellationToken);
}
