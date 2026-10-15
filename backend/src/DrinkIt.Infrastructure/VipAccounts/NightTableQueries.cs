using DrinkIt.Application.Nights;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.VipAccounts;

/// <summary>Read side: no tracking, a projection straight to the line, joined with each table.</summary>
internal sealed class NightTableQueries(DrinkItDbContext context) : INightTableQueries
{
    public async Task<IReadOnlyList<NightTableLine>?> ListAsync(Guid nightId, CancellationToken cancellationToken)
    {
        // Asked first so another venue's night reads as not found, the same as
        // one that does not exist, instead of as a night with no tables.
        if (!await context.Nights.AnyAsync(night => night.Id == nightId, cancellationToken)) return null;

        return await context.VipAccounts
            .AsNoTracking()
            .Where(account => account.NightId == nightId)
            .Join(context.Tables, account => account.TableId, table => table.Id, (account, table) => new { account, table })
            .OrderBy(row => row.table.Number)
            .Select(row => new NightTableLine(row.table.Id, row.table.Number, row.table.Capacity, row.table.Location, row.account.Budget, row.account.Spent))
            .ToListAsync(cancellationToken);
    }
}
