using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.VipAccounts;

/// <summary>
/// The write side of the VIP account aggregate: a table in one night with its
/// budget (US-40). A repository because the balance is money with a
/// concurrency token to protect.
/// </summary>
/// <remarks>
/// Adding and removing only track the change. The night's form saves the
/// night and its tables together, so they are committed by the night's own
/// save in the same request — one unit of work, all or nothing.
/// </remarks>
public interface IVipAccountRepository
{
    /// <summary>The accounts of a night of the current venue, tracked so they can change.</summary>
    Task<IReadOnlyList<VipAccount>> ListForNightAsync(Guid nightId, CancellationToken cancellationToken);

    void Add(VipAccount account);

    void Remove(VipAccount account);

    /// <summary>
    /// Whether the table has an account in a night that has not ended at
    /// <paramref name="now"/>, upcoming or underway. Such a table is not put
    /// away (US-39): somebody bought it.
    /// </summary>
    Task<bool> InANightNotOverAsync(Guid tableId, DateTimeOffset now, CancellationToken cancellationToken);
}
