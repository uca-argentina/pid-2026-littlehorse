using DrinkIt.Application.Nights;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Application.Tests.Nights;

/// <summary>
/// The night stock repository over lists. It holds one venue's rows, as the
/// real one does through the global query filter.
/// </summary>
internal sealed class NightStocksInMemory(NightForStock? night, params StockedProduct[] products) : INightStockRepository
{
    public List<NightStock> Existing { get; } = [];

    public List<NightStock> Added { get; } = [];

    /// <summary>What the latest earlier night with a row left, by product.</summary>
    public Dictionary<Guid, int> CarriedOver { get; } = [];

    public Task<NightForStock?> FindNightAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult(night is not null && night.Id == nightId ? night : null);

    public Task<IReadOnlyList<NightStock>> ListAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NightStock>>([.. Existing, .. Added]);

    public Task<IReadOnlyList<StockedProduct>> ListActiveProductsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockedProduct>>(products);

    public Task<IReadOnlyDictionary<Guid, int>> CarriedOverAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(CarriedOver);

    public Task AddRangeAsync(IReadOnlyCollection<NightStock> rows, CancellationToken cancellationToken)
    {
        Added.AddRange(rows);

        return Task.CompletedTask;
    }
}
