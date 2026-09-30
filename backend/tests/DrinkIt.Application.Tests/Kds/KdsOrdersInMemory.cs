using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Kds;

/// <summary>
/// The order repository as the staff's and the customer's use cases see it:
/// find one, change it, save it. Shared by the handler specs so they do not
/// each carry a copy of the same double.
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

    public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.SingleOrDefault(order => order.TrackingToken == token));

    private bool _refusesTheNextSave;

    /// <summary>Loses the next save to somebody else's, the way two screens racing do.</summary>
    public void RefusesTheNextSave() => _refusesTheNextSave = true;

    public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken)
    {
        if (_refusesTheNextSave)
        {
            _refusesTheNextSave = false;

            return Task.FromResult<Result<Order>>(OrderErrors.ChangedMeanwhile);
        }

        Saves += 1;
        return Task.FromResult<Result<Order>>(order);
    }

    /// <summary>How many cancellations were committed, drinks put back on the shelf included.</summary>
    public int Cancellations { get; private set; }

    public Task<Result<Order>> SaveCancellationAsync(Order order, CancellationToken cancellationToken)
    {
        if (_refusesTheNextSave)
        {
            _refusesTheNextSave = false;

            return Task.FromResult<Result<Order>>(OrderErrors.ChangedMeanwhile);
        }

        Cancellations += 1;
        return Task.FromResult<Result<Order>>(order);
    }

    public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The bar never places an order.");

    public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The bar never places an order.");
}
