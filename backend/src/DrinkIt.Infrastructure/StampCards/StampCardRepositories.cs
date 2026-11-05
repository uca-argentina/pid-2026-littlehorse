using DrinkIt.Application.StampCards;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.StampCards;
using DrinkIt.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DrinkIt.Infrastructure.StampCards;

internal sealed class StampCardRepository(DrinkItDbContext context) : IStampCardRepository
{
    /// <summary>The venue filter adds the venue, so another venue's token is null.</summary>
    public Task<StampCard?> GetForUpdateAsync(string? token, CancellationToken cancellationToken) =>
        StampCard.IsWellShaped(token)
            ? context.StampCards.FirstOrDefaultAsync(card => card.Token == token, cancellationToken)
            : Task.FromResult<StampCard?>(null);

    public void Add(StampCard card) => context.StampCards.Add(card);
}

internal sealed class StampCardRuleRepository(DrinkItDbContext context) : IStampCardRuleRepository
{
    public Task<StampCardRule?> GetForUpdateAsync(CancellationToken cancellationToken) =>
        context.StampCardRules.SingleOrDefaultAsync(cancellationToken);

    public async Task AddAsync(StampCardRule rule, CancellationToken cancellationToken)
    {
        context.StampCardRules.Add(rule);

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}

internal sealed class StampCardQueries(DrinkItDbContext context) : IStampCardQueries
{
    public Task<StampCardRule?> GetRuleAsync(CancellationToken cancellationToken) =>
        context.StampCardRules.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Derived from the orders of the card, as US-58 asks: earned is the units
    /// of what was delivered, rewards excluded; spent is what the reward lines
    /// cost. An order that is not delivered, or was canceled, gives nothing, so
    /// undoing a delivery and canceling a cash order need no compensation.
    /// </summary>
    public async Task<StampBalance> GetBalanceAsync(Guid cardId, Guid? currentNight, CancellationToken cancellationToken)
    {
        IQueryable<Order> orders = context.Orders.AsNoTracking().Where(order => order.StampCardId == cardId);

        int earned = await orders
            .Where(order => order.Status == OrderStatus.Delivered)
            .SelectMany(order => order.Items)
            .Where(item => item.StampsSpent == 0)
            .SumAsync(item => (int?)item.Quantity, cancellationToken) ?? 0;

        // Cash waiting at the till holds its stamps only during its own night:
        // one that was never collected from a night that is over does not
        // (backlog note of US-59, until the open question of section 15 closes).
        int spent = await orders
            .Where(order => order.Status != OrderStatus.Canceled)
            .Where(order => order.Status != OrderStatus.AwaitingPayment || order.NightId == currentNight)
            .SelectMany(order => order.Items)
            .SumAsync(item => (int?)item.StampsSpent, cancellationToken) ?? 0;

        return new StampBalance(earned, spent);
    }

    public async Task<Guid?> FindCardIdAsync(string? token, CancellationToken cancellationToken)
    {
        if (!StampCard.IsWellShaped(token)) return null;

        return await context.StampCards
            .AsNoTracking()
            .Where(card => card.Token == token)
            .Select(card => (Guid?)card.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<string?> GetTokenAsync(Guid cardId, CancellationToken cancellationToken) =>
        context.StampCards
            .AsNoTracking()
            .Where(card => card.Id == cardId)
            .Select(card => card.Token)
            .FirstOrDefaultAsync(cancellationToken);
}
