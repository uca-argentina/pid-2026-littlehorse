using DrinkIt.Application.Common;
using DrinkIt.Application.VipAccounts;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tables;

/// <summary>
/// Puts a table away. The row stays: old orders keep saying "Mesa 5", and the
/// number stays taken so no other table can end up with it.
/// </summary>
/// <remarks>
/// US-40: a table sold for a night still to come or underway is not put
/// away. Once that night ends, it can be.
/// </remarks>
public sealed class DeactivateTableHandler(ITableRepository tables, IVipAccountRepository vipAccounts, TimeProvider clock)
{
    public async Task<Result<TableSummary>> HandleAsync(Guid tableId, CancellationToken cancellationToken)
    {
        Table? table = await tables.GetForUpdateAsync(tableId, cancellationToken);

        if (table is null) return TableErrors.NotFound;
        if (await vipAccounts.InANightNotOverAsync(table.Id, clock.GetUtcNow(), cancellationToken)) return TableErrors.InANightNotOver;

        table.Deactivate();

        await tables.SaveChangesAsync(cancellationToken);

        return TableSummary.Of(table);
    }
}
