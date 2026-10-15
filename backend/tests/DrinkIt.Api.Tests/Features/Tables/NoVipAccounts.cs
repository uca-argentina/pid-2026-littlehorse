using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.VipAccounts;

namespace DrinkIt.Api.Tests.Features.Tables;

/// <summary>A venue where no table has been sold for any night.</summary>
internal sealed class NoVipAccounts : IVipAccountRepository
{
    public Task<IReadOnlyList<VipAccount>> ListForNightAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VipAccount>>([]);

    public Task<VipAccount?> FindByAccessCodeAsync(string accessCode, CancellationToken cancellationToken) =>
        Task.FromResult<VipAccount?>(null);

    public void Add(VipAccount account)
    {
    }

    public void Remove(VipAccount account)
    {
    }

    public Task<bool> InANightNotOverAsync(Guid tableId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
