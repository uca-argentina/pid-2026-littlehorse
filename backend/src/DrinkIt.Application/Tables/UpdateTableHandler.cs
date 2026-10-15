using DrinkIt.Application.Common;
using DrinkIt.Domain.Tables;

namespace DrinkIt.Application.Tables;

public sealed record UpdateTableCommand(Guid TableId, int Capacity, string? Location);

/// <summary>
/// Corrects how many people a table seats and where it is. The number is not
/// on offer: it is what the orders placed from it say.
/// </summary>
public sealed class UpdateTableHandler(ITableRepository tables)
{
    public async Task<Result<TableSummary>> HandleAsync(UpdateTableCommand command, CancellationToken cancellationToken)
    {
        Table? table = await tables.GetForUpdateAsync(command.TableId, cancellationToken);

        if (table is null) return TableErrors.NotFound;

        // Out of range is a broken domain invariant: Describe throws before
        // touching anything, and the API turns it into a 400.
        table.Describe(command.Capacity, command.Location);

        await tables.SaveChangesAsync(cancellationToken);

        return TableSummary.Of(table);
    }
}
