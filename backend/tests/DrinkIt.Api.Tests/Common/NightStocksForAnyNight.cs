using DrinkIt.Application.Nights;
using DrinkIt.Domain.Menu;
using DrinkIt.Domain.Nights;

namespace DrinkIt.Api.Tests.Common;

/// <summary>
/// The night stock repository for endpoint tests: whatever night is asked about
/// is the venue's first, so every product starts with its own number. What
/// opening does is proved in the application tests; here the endpoint only
/// needs something that answers.
/// </summary>
internal sealed class NightStocksForAnyNight(params Product[] products) : INightStockRepository
{
    private readonly List<NightStock> _rows = [];

    public IReadOnlyList<NightStock> Rows => _rows;

    public Task<NightForStock?> FindNightAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult<NightForStock?>(new(nightId, DateTimeOffset.UtcNow.AddHours(-1), PreviousEndedAt: null));

    public Task<IReadOnlyList<NightStock>> ListAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NightStock>>([.. _rows.Where(row => row.NightId == nightId)]);

    public Task<IReadOnlyList<StockedProduct>> ListActiveProductsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockedProduct>>(
            [.. products.Select(product => new StockedProduct(product.Id, product.Name, product.InitialStock))]);

    public Task<IReadOnlyDictionary<Guid, int>> CarriedOverAsync(Guid nightId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

    public Task<NightStock?> GetForUpdateAsync(Guid nightId, Guid productId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.Find(row => row.NightId == nightId && row.ProductId == productId));

    public Task<bool> SaveAdjustmentAsync(NightStock stock, int change, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task AddRangeAsync(IReadOnlyCollection<NightStock> rows, CancellationToken cancellationToken)
    {
        _rows.AddRange(rows);

        return Task.CompletedTask;
    }
}
