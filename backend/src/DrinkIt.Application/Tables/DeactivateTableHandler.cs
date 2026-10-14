using DrinkIt.Application.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tables;

/// <summary>
/// Puts a table away. The row stays: old orders keep saying "Mesa 5", and the
/// number stays taken so no other table can end up with it.
/// </summary>
/// <remarks>
/// US-40 adds a rule here: a table with an open account is not put away. There
/// are no accounts yet, so there is nothing to ask.
/// </remarks>
public sealed class DeactivateTableHandler(ITableRepository tables)
{
    public async Task<Result<TableSummary>> HandleAsync(Guid tableId, CancellationToken cancellationToken)
    {
        Table? table = await tables.GetForUpdateAsync(tableId, cancellationToken);

        if (table is null) return TableErrors.NotFound;

        table.Deactivate();

        await tables.SaveChangesAsync(cancellationToken);

        return TableSummary.Of(table);
    }
}
