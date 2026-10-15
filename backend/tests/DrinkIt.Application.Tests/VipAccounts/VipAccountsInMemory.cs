using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Application.Tests.VipAccounts;

/// <summary>The VIP account repository over a list, for one venue, like the other in-memory ones.</summary>
internal sealed class VipAccountsInMemory(params VipAccount[] stored) : IVipAccountRepository
{
    private readonly List<VipAccount> _stored = [.. stored];

    public List<VipAccount> Added { get; } = [];

    public List<VipAccount> Removed { get; } = [];

    /// <summary>The tables with an account in a night not over, as the database would answer.</summary>
    public HashSet<Guid> TablesInANightNotOver { get; } = [];

    public IReadOnlyList<VipAccount> Stored => _stored;

    public Task<IReadOnlyList<VipAccount>> ListForNightAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VipAccount>>([.. _stored.Where(account => account.NightId == nightId)]);

    public void Add(VipAccount account)
    {
        Added.Add(account);
        _stored.Add(account);
    }

    public void Remove(VipAccount account)
    {
        Removed.Add(account);
        _stored.Remove(account);
    }

    public Task<bool> InANightNotOverAsync(Guid tableId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(TablesInANightNotOver.Contains(tableId));
}
