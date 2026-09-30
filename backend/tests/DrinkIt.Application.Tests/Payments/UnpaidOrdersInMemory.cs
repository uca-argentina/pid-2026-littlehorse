using DrinkIt.Application.Common;
using DrinkIt.Application.Orders;
using DrinkIt.Domain.Orders;

namespace DrinkIt.Application.Tests.Payments;

/// <summary>
/// The order repository as the payment use cases see it: find an order by
/// its id, its code or its age, move it, save it — with or without giving its
/// stock back. Holds one venue's orders only, like the real one behind the
/// global query filter.
/// </summary>
internal sealed class UnpaidOrdersInMemory(params Order[] stored) : IOrderRepository
{
    private readonly List<Order> _stored = [.. stored];

    /// <summary>When each order was placed, as the persistence layer would stamp it.</summary>
    public Dictionary<Guid, DateTimeOffset> PlacedAt { get; } = [];

    public List<Order> Saved { get; } = [];

    public List<Order> StockReturned { get; } = [];

    public Task<Order?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.SingleOrDefault(order => order.Id == id));

    public Task<Order?> GetForUpdateAsync(OrderCode code, CancellationToken cancellationToken) =>
        Task.FromResult(_stored.SingleOrDefault(order => order.Code == code));

    public Task<IReadOnlyList<Order>> GetAwaitingPaymentCreatedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Order>>(
        [
            .. _stored.Where(order =>
                order.Status == OrderStatus.AwaitingPayment
                && PlacedAt.TryGetValue(order.Id, out DateTimeOffset placed)
                && placed < before),
        ]);

    public Task<Result<Order>> SaveAsync(Order order, CancellationToken cancellationToken)
    {
        Saved.Add(order);
        return Task.FromResult<Result<Order>>(order);
    }

    public Task SaveReturningStockAsync(Order order, CancellationToken cancellationToken)
    {
        StockReturned.Add(order);
        return Task.CompletedTask;
    }

    public Task<Order?> GetForUpdateAsync(TrackingToken token, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A payment is never found by the pickup QR.");

    public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A payment never places an order.");

    public Task<Result<Order>> AddAsync(Order order, string idempotencyKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A payment never places an order.");
}
