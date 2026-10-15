using DrinkIt.Application.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tables;

/// <summary>
/// Brings back a table that was put away, with the number it always had.
/// Creating it again would be refused: the number never stopped being taken.
/// </summary>
public sealed class ReactivateTableHandler(ITableRepository tables)
{
    public async Task<Result<TableSummary>> HandleAsync(Guid tableId, CancellationToken cancellationToken)
    {
        Table? table = await tables.GetForUpdateAsync(tableId, cancellationToken);

        if (table is null) return TableErrors.NotFound;

        table.Activate();

        await tables.SaveChangesAsync(cancellationToken);

        return TableSummary.Of(table);
    }
}
