using DrinkIt.Application.VipAccounts;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.VipAccounts;

/// <summary>Read side: no tracking, the venue from the global query filter.</summary>
internal sealed class VipAccountQueries(DrinkItDbContext context) : IVipAccountQueries
{
    public Task<VipTableAccess?> FindOpenAsync(string accessCode, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.VipAccounts
            .AsNoTracking()
            .Where(account => account.AccessCode == accessCode)
            .Where(account => context.Nights.Any(night => night.Id == account.NightId && night.StartsAt <= now && now < night.EndsAt))
            .Join(context.Tables, account => account.TableId, table => table.Id, (account, table) => new VipTableAccess(table.Number, account.Budget - account.Spent))
            .FirstOrDefaultAsync(cancellationToken);
}
