using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.VipAccounts;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.VipAccounts;

/// <summary>
/// Adds and removes only track: the night's repository commits them with the
/// night, through the same scoped context, so the form saves all or nothing.
/// </summary>
internal sealed class VipAccountRepository(DrinkItDbContext context) : IVipAccountRepository
{
    /// <summary>No venue in the WHERE: the global query filter adds it.</summary>
    public async Task<IReadOnlyList<VipAccount>> ListForNightAsync(Guid nightId, CancellationToken cancellationToken) =>
        await context.VipAccounts.Where(account => account.NightId == nightId).ToListAsync(cancellationToken);

    /// <summary>No venue in the WHERE: the global query filter adds the one the slug resolved.</summary>
    public Task<VipAccount?> FindByAccessCodeAsync(string accessCode, CancellationToken cancellationToken) =>
        context.VipAccounts.FirstOrDefaultAsync(account => account.AccessCode == accessCode, cancellationToken);

    public void Add(VipAccount account) => context.VipAccounts.Add(account);

    public void Remove(VipAccount account) => context.VipAccounts.Remove(account);

    public Task<bool> InANightNotOverAsync(Guid tableId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.VipAccounts.AnyAsync(
            account => account.TableId == tableId
                && context.Nights.Any(night => night.Id == account.NightId && night.EndsAt > now),
            cancellationToken);
}
