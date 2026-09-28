using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Kds;

/// <summary>
/// The order repository as the bar's use cases see it: find one by its code,
/// change it, save it. Shared by the KDS handler specs so they do not each
/// carry a copy of the same double.
/// </summary>
/// <remarks>
/// It only ever holds one venue's orders, like the real one behind the global
/// query filter: an order from somewhere else simply is not found.
/// </remarks>
internal sealed class KdsOrdersInMemory(params Order[] stored) : IOrderRepository
{
    private readonly List<Order> _stored = [.. stored];

    /// <summary>How many times an order was committed.</summary>
    public int Saves { get; private set; }

    public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.SingleOrDefault(order => order.Code == code));

    public Task SaveAsync(Order order, CancellationToken cancellationToken)
    {
        Saves += 1;
        return Task.CompletedTask;
    }

    public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The bar never places an order.");

    public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The bar never places an order.");
}
